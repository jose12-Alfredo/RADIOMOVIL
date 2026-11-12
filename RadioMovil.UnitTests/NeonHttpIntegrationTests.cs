using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RadioMovil.Infrastructure;
using RADIOMOVIL;
using Xunit;

namespace RadioMovil.UnitTests;

// TestServer procesa HTTP real contra Npgsql/Neon Testing, nunca usa una base en memoria.
public sealed class NeonHttpIntegrationTests
{
    private sealed class Host : IDisposable
    {
        public Guid Empresa { get; } = Guid.NewGuid();
        public WebApplicationFactory<Program> Factory { get; }
        public HttpClient Client { get; }
        public Host(bool databaseUnavailable = false)
        {
            Environment.SetEnvironmentVariable("NEON_TESTING_CONNECTION_STRING", databaseUnavailable
                ? "Host=127.0.0.1;Port=1;Database=unreachable;Username=probe;Password=local;SSL Mode=VerifyFull;Timeout=1"
                : Environment.GetEnvironmentVariable("NEON_TESTING_DIRECT_CONNECTION_STRING"));
            Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(x => x.UseEnvironment("Testing"));
            Client = Factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        }
        public async Task LoginAsync()
        {
            var identity = await TestIdentity.SeedAsync(Factory, Empresa);
            using var _ = await TestIdentity.LoginAndroidAsync(Client, identity);
        }
        public void Dispose()
        {
            Client.Dispose(); Factory.Dispose();
            Environment.SetEnvironmentVariable("NEON_TESTING_CONNECTION_STRING", null);
        }
    }

    private static async Task<(HttpResponseMessage response, JsonDocument json)> CreateAsync(Host host, Guid central, string origen, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/solicitudes")
        {
            Content = JsonContent.Create(new { centralId = central, origen })
        };
        request.Headers.Add("Idempotency-Key", key);
        var response = await host.Client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        return (response, JsonDocument.Parse(response.Content.Headers.ContentType?.MediaType == "application/json"
            ? text : JsonSerializer.Serialize(text)));
    }

    [NeonFact]
    public async Task Http_CrearLeerRepetirYConflictoNoDuplica()
    {
        using var host = new Host(); await host.LoginAsync(); var central = Guid.NewGuid(); var key = Guid.NewGuid().ToString("N");
        using (var setupScope = host.Factory.Services.CreateScope()) await TestCentrales.CreateAsync(setupScope.ServiceProvider.GetRequiredService<RadioMovilDbContext>(), host.Empresa, central);
        var (created, json) = await CreateAsync(host, central, "Terminal", key);
        using (created) using (json)
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            Assert.True(created.Headers.TryGetValues("Server-Timing", out var timings));
            Assert.Contains("counter_sql;dur=", string.Join(",", timings));
            Assert.DoesNotContain("read_sql;dur=", string.Join(",", timings));
            Assert.DoesNotContain(key, string.Join(",", timings));
            var id = json.RootElement.GetProperty("id").GetGuid();
            Assert.Equal(host.Empresa, json.RootElement.GetProperty("empresaId").GetGuid());
            Assert.Equal(central, json.RootElement.GetProperty("centralId").GetGuid());
            Assert.Equal(1, json.RootElement.GetProperty("ordenRecepcion").GetInt64());
            Assert.NotEqual(default, json.RootElement.GetProperty("creadaEn").GetDateTimeOffset());
            using var loaded = await host.Client.GetAsync($"/api/v1/solicitudes/{id}");
            Assert.Equal(HttpStatusCode.OK, loaded.StatusCode);
            var (replayed, same) = await CreateAsync(host, central, "Terminal", key);
            using (replayed) using (same)
            {
                Assert.Equal(HttpStatusCode.OK, replayed.StatusCode);
                Assert.Contains("read_sql;dur=", string.Join(",", replayed.Headers.GetValues("Server-Timing")));
                Assert.Equal(id, same.RootElement.GetProperty("id").GetGuid());
            }
            var (conflict, body) = await CreateAsync(host, central, "Otro origen", key);
            using (conflict) using (body) Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            using var scope = host.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioMovilDbContext>();
            Assert.Equal(1, await db.Solicitudes.CountAsync(x => x.EmpresaId == host.Empresa));
            Assert.Equal(1, await db.Eventos.CountAsync(x => x.SolicitudId == id));
        }
    }

    [NeonFact]
    public async Task Http_CienSolicitudesSimultaneas_OrdenYFiltros()
    {
        using var host = new Host(); await host.LoginAsync(); var central = Guid.NewGuid();
        using (var setupScope = host.Factory.Services.CreateScope()) await TestCentrales.CreateAsync(setupScope.ServiceProvider.GetRequiredService<RadioMovilDbContext>(), host.Empresa, central);
        // 100 tareas concurrentes, máximo 5 operaciones de DB activas por el contador serializado.
        using var gate = new SemaphoreSlim(5);
        async Task<(HttpStatusCode status, Guid id, long order)> SendAsync(int number)
        {
            await gate.WaitAsync();
            try
            {
                var (response, json) = await CreateAsync(host, central, $"Origen {number}", Guid.NewGuid().ToString("N"));
                using (response) using (json)
                    return (response.StatusCode, json.RootElement.GetProperty("id").GetGuid(), json.RootElement.GetProperty("ordenRecepcion").GetInt64());
            }
            finally { gate.Release(); }
        }
        var results = await Task.WhenAll(Enumerable.Range(0, 100).Select(SendAsync));
        Assert.All(results, x => Assert.Equal(HttpStatusCode.Created, x.status));
        Assert.Equal(100, results.Select(x => x.id).Distinct().Count());
        Assert.Equal(Enumerable.Range(1, 100).Select(x => (long)x), results.Select(x => x.order).Order());
        using var page = await host.Client.GetAsync($"/api/v1/solicitudes?centralId={central}&estado=Pendiente&page=2&pageSize=20");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        using var listed = JsonDocument.Parse(await page.Content.ReadAsStringAsync());
        Assert.Equal(100, listed.RootElement.GetProperty("total").GetInt32());
        var items = listed.RootElement.GetProperty("items");
        Assert.Equal(20, items.GetArrayLength());
        Assert.Equal(21, items[0].GetProperty("ordenRecepcion").GetInt64());
        using var scope = host.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioMovilDbContext>();
        Assert.Equal(100, await db.Solicitudes.CountAsync(x => x.EmpresaId == host.Empresa));
        foreach (var id in results.Select(x => x.id)) Assert.True(await db.Solicitudes.AnyAsync(x => x.Id == id));
    }

    [NeonFact]
    public async Task Http_SinPermisoOtroTenantYErroresNoConfirman()
    {
        using var host = new Host(); await host.LoginAsync();
        using var anonymous = host.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/solicitudes")).StatusCode);
        var other = Guid.NewGuid();
        Guid otherId;
        using (var scope = host.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioMovilDbContext>();
            var otherCentral = await TestCentrales.CreateAsync(db, other);
            (otherId, _) = await new RegistroSolicitudes(db).RegistrarAsync(other, otherCentral, Guid.NewGuid(), "Ajena", Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync($"/api/v1/solicitudes/{otherId}")).StatusCode);
        using var list = await host.Client.GetAsync("/api/v1/solicitudes");
        using var data = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        Assert.Equal(0, data.RootElement.GetProperty("total").GetInt32());
        var (invalid, body) = await CreateAsync(host, Guid.Empty, "Origen", Guid.NewGuid().ToString("N"));
        using (invalid) using (body) Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.GetAsync("/api/v1/solicitudes?pageSize=200")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.GetAsync("/api/v1/solicitudes?estado=Invalido")).StatusCode);
        using var check = host.Factory.Services.CreateScope();
        Assert.False(await check.ServiceProvider.GetRequiredService<RadioMovilDbContext>().Solicitudes.AnyAsync(x => x.EmpresaId == host.Empresa));
    }

    [NeonFact]
    public async Task Http_BaseNoDisponible_NuncaConfirmaSolicitud()
    {
        using var host = new Host(databaseUnavailable: true);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/solicitudes")
        {
            Content = JsonContent.Create(new { centralId = Guid.NewGuid(), origen = "Terminal" })
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
