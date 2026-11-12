using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RadioMovil.Infrastructure;
using RADIOMOVIL;
using Xunit;

namespace RadioMovil.UnitTests;

// Opt-in: run in its own test process, never alongside other tests modifying process environment.
public sealed class NeonConcurrencyBenchmarks
{
    private sealed record Attempt(string Key, string Origin, HttpStatusCode? Status, double Ms, Guid? Id,
        long? Order, Dictionary<string, double> Stages, string? Error);

    [NeonFact]
    public async Task CompareDirectAndPooler()
    {
        if (Environment.GetEnvironmentVariable("RM_RUN_BENCHMARK") != "1") return;
        var direct = Environment.GetEnvironmentVariable("NEON_TESTING_DIRECT_CONNECTION_STRING")!;
        var pooled = Environment.GetEnvironmentVariable("NEON_TESTING_POOLED_CONNECTION_STRING")!;
        var directSettings = new NpgsqlConnectionStringBuilder(NeonTestingGuard.RequireTesting(direct, requireDirect: true));
        var pooledSettings = new NpgsqlConnectionStringBuilder(NeonTestingGuard.RequireTesting(pooled));
        Assert.Contains("-pooler", pooledSettings.Host!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(directSettings.Host!.Replace("-pooler", "", StringComparison.OrdinalIgnoreCase),
            pooledSettings.Host!.Replace("-pooler", "", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(directSettings.Database, pooledSettings.Database);
        Assert.Equal(directSettings.Username, pooledSettings.Username);
        var levels = Environment.GetEnvironmentVariable("RM_BENCH_20") == "1" ? new[] { 20 } : new[] { 1, 5, 10 };
        var repetitions = 2;
        var integrity = true;
        var file = Environment.GetEnvironmentVariable("RM_BENCH_OUTPUT") ?? throw new InvalidOperationException("Ruta externa RM_BENCH_OUTPUT obligatoria.");
        foreach (var (mode, raw) in new[] { ("direct", direct), ("pooled", pooled) })
        {
            var settings = new NpgsqlConnectionStringBuilder(NeonConnection.Validate(raw)) { ApplicationName = "rm-bench" };
            var empresa = Guid.NewGuid();
            Environment.SetEnvironmentVariable("NEON_TESTING_CONNECTION_STRING", settings.ConnectionString);
            using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(x => x.UseEnvironment("Testing"));
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            var identity = await TestIdentity.SeedAsync(factory, empresa);
            using var tokens = await TestIdentity.LoginAndroidAsync(client, identity);
            try
            {
                // Warm the compute and Npgsql pool before measurement; use a separate central.
                await Batch(client, Guid.NewGuid(), 5, 1);
                foreach (var level in levels)
                {
                    // Pre-warm up to this concurrency before either timed repetition.
                    await Batch(client, Guid.NewGuid(), Math.Max(5, level), level);
                    for (var repeat = 1; repeat <= repetitions; repeat++)
                    {
                        var central = Guid.NewGuid();
                        using var observerCancel = new CancellationTokenSource();
                        var observer = Observe(direct, observerCancel.Token);
                        var attempts = await Batch(client, central, 100, level);
                        observerCancel.Cancel();
                        var observed = await observer;
                        using var scope = factory.Services.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<RadioMovilDbContext>();
                        var rows = await db.Solicitudes.AsNoTracking().Where(x => x.EmpresaId == empresa &&
                            EF.Property<Guid>(x, "CentralId") == central)
                            .Select(x => new { x.Id, Order = EF.Property<long>(x, "OrdenRecepcion") }).ToListAsync();
                        var sentKeys = attempts.Select(x => x.Key).ToArray();
                        var keys = await db.Idempotencias.AsNoTracking().Where(x => x.EmpresaId == empresa && x.Ambito == "registro_solicitud")
                            .Select(x => new { x.Clave, x.SolicitudId }).Where(x => sentKeys.Contains(x.Clave)).ToListAsync();
                        var uncertain = attempts.Where(x => x.Status != HttpStatusCode.Created).ToList();
                        var reconciled = new List<object>();
                        foreach (var a in uncertain)
                        {
                            var replay = await Send(client, central, a.Origin, a.Key);
                            reconciled.Add(new { initial = (int?)a.Status, a.Error, retry = (int?)replay.Status,
                                persisted = replay.Id.HasValue && rows.Any(x => x.Id == replay.Id) });
                        }
                        var afterOrders = await db.Solicitudes.AsNoTracking().Where(x => x.EmpresaId == empresa &&
                            EF.Property<Guid>(x, "CentralId") == central)
                            .Select(x => EF.Property<long>(x, "OrdenRecepcion")).ToListAsync();
                        var afterRetry = afterOrders.Count;
                        var afterRetryKeys = await db.Idempotencias.AsNoTracking()
                            .CountAsync(x => x.EmpresaId == empresa && x.Ambito == "registro_solicitud" && sentKeys.Contains(x.Clave));
                        var latencies = attempts.Select(x => x.Ms).Order().ToArray();
                        var stages = attempts.SelectMany(x => x.Stages).GroupBy(x => x.Key)
                            .ToDictionary(x => x.Key, x => new { mean = x.Average(y => y.Value), p95 = Percentile(x.Select(y => y.Value).Order().ToArray(), .95) });
                        var line = JsonSerializer.Serialize(new
                        {
                            mode, level, repeat, poolEnabled = settings.Pooling, maxPoolSize = settings.MaxPoolSize,
                            commandTimeout = settings.CommandTimeout, sent = attempts.Length,
                            confirmed = attempts.Count(x => x.Status == HttpStatusCode.Created),
                            errors = attempts.GroupBy(x => x.Error ?? ((int?)x.Status)?.ToString() ?? "unknown").ToDictionary(x => x.Key, x => x.Count()),
                            persisted = rows.Count, keys = keys.Count,
                            orphanKeys = keys.Count(x => !rows.Any(y => y.Id == x.SolicitudId)),
                            afterRetry, afterRetryKeys,
                            afterRetryOrdered = afterOrders.Order().SequenceEqual(Enumerable.Range(1, 100).Select(x => (long)x)),
                            distinctIds = rows.Select(x => x.Id).Distinct().Count(),
                            ordersSequential = rows.Select(x => x.Order).Order().SequenceEqual(Enumerable.Range(1, 100).Select(x => (long)x)),
                            mean = latencies.Average(), p95 = Percentile(latencies, .95), p99 = Percentile(latencies, .99),
                            stages, observed, reconciled
                        });
                        File.AppendAllText(file, line + Environment.NewLine);
                        // Keep measuring other combinations even after a timeout; fail at the end if
                        // replays fail to restore exactly one row per key.
                        integrity &= afterRetry == 100 && afterRetryKeys == 100 &&
                            afterOrders.Order().SequenceEqual(Enumerable.Range(1, 100).Select(x => (long)x)) &&
                            rows.Select(x => x.Id).Distinct().Count() == rows.Count &&
                            rows.Select(x => x.Order).Distinct().Count() == rows.Count;
                    }
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable("NEON_TESTING_CONNECTION_STRING", null);
            }
        }
        Assert.True(integrity, "Persistencia o idempotencia incoherente tras los reintentos; revisar JSONL.");
    }

    private static async Task<Attempt[]> Batch(HttpClient client, Guid central, int count, int concurrency)
    {
        using var gate = new SemaphoreSlim(concurrency);
        return await Task.WhenAll(Enumerable.Range(0, count).Select(async i =>
        {
            await gate.WaitAsync();
            try { return await Send(client, central, $"bench-{i}", Guid.NewGuid().ToString("N")); }
            finally { gate.Release(); }
        }));
    }

    private static async Task<Attempt> Send(HttpClient client, Guid central, string origin, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/solicitudes")
        { Content = JsonContent.Create(new { centralId = central, origen = origin }) };
        request.Headers.Add("Idempotency-Key", key);
        var start = Stopwatch.GetTimestamp();
        try
        {
            using var response = await client.SendAsync(request);
            var ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            var timing = new Dictionary<string, double>();
            if (response.Headers.TryGetValues("Server-Timing", out var values))
                foreach (var part in string.Join(",", values).Split(','))
                {
                    var fields = part.Split(";dur=");
                    if (fields.Length == 2 && double.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var duration))
                        timing[fields[0]] = duration;
                }
            Guid? id = null; long? order = null;
            if (response.IsSuccessStatusCode)
            {
                using var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                id = data.RootElement.GetProperty("id").GetGuid();
                order = data.RootElement.GetProperty("ordenRecepcion").GetInt64();
            }
            return new(key, origin, response.StatusCode, ms, id, order, timing,
                response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex) { return new(key, origin, null, Stopwatch.GetElapsedTime(start).TotalMilliseconds, null, null, new(), ex.GetType().Name); }
    }

    private static double Percentile(double[] values, double p) => values[Math.Min(values.Length - 1, (int)Math.Ceiling(values.Length * p) - 1)];

    private static async Task<object> Observe(string direct, CancellationToken token)
    {
        var samples = 0; var blocked = 0; var waits = new Dictionary<string, int>();
        try
        {
            await using var connection = new NpgsqlConnection(NeonConnection.Validate(direct, requireDirect: true));
            await connection.OpenAsync(token);
            while (!token.IsCancellationRequested)
            {
                await using var command = new NpgsqlCommand("""
                    SELECT coalesce(wait_event_type, 'running'), count(*),
                           count(*) FILTER (WHERE cardinality(pg_blocking_pids(pid)) > 0)
                    FROM pg_stat_activity
                    WHERE datname = current_database() AND usename = current_user
                      AND pid <> pg_backend_pid() AND state <> 'idle'
                    GROUP BY 1
                    """, connection);
                await using var reader = await command.ExecuteReaderAsync(token);
                samples++;
                while (await reader.ReadAsync(token))
                {
                    var name = reader.GetString(0);
                    waits[name] = waits.GetValueOrDefault(name) + (int)reader.GetInt64(1);
                    blocked += (int)reader.GetInt64(2);
                }
                await Task.Delay(200, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { return new { samples, blocked, waits, observerError = ex.GetType().Name }; }
        return new { samples, blocked, waits, observerError = (string?)null };
    }
}
