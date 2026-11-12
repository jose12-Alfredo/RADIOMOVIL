using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RadioMovil.Domain;
using RadioMovil.Infrastructure;
using RADIOMOVIL;
using Xunit;

namespace RadioMovil.UnitTests;

public sealed class DespachoHttpTests
{
    private sealed class TestingHost : IDisposable
    {
        public Guid Empresa { get; } = Guid.NewGuid();
        public Guid Actor { get; } = Guid.NewGuid();
        public WebApplicationFactory<Program> Factory { get; }
        public HttpClient Client { get; }
        public TestingHost()
        {
            Environment.SetEnvironmentVariable("NEON_TESTING_CONNECTION_STRING", Environment.GetEnvironmentVariable("NEON_TESTING_DIRECT_CONNECTION_STRING"));
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
            foreach (var name in new[] { "NEON_TESTING_CONNECTION_STRING" })
                Environment.SetEnvironmentVariable(name, null);
        }
    }

    private static async Task<HttpResponseMessage> Post(HttpClient client, string path, object body, string? key)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        if (key is not null) req.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(req);
    }

    [NeonFact]
    public async Task Http_OperadorTesting_EmpresaAislada_IdempotenciaYViaje()
    {
        using var host = new TestingHost();
        await host.LoginAsync();
        Guid id; Guid driver; Guid otherDriver; Guid foreign;
        using (var scope = host.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioMovilDbContext>();
            var a = new Conductor(host.Empresa, "Candidato A"); var b = new Conductor(host.Empresa, "Candidato B");
            var va = new Vehiculo(host.Empresa, "HT" + Guid.NewGuid().ToString("N")[..9]);
            var vb = new Vehiculo(host.Empresa, "HT" + Guid.NewGuid().ToString("N")[..9]);
            db.Conductores.AddRange(a, b); db.Vehiculos.AddRange(va, vb);
            db.Turnos.AddRange(new Turno(host.Empresa, a.Id, va.Id, DateTimeOffset.UtcNow.AddMinutes(-1)),
                new Turno(host.Empresa, b.Id, vb.Id, DateTimeOffset.UtcNow.AddMinutes(-1)));
            await db.SaveChangesAsync();
            var central = await TestCentrales.CreateAsync(db, host.Empresa);
            (id, _) = await new RegistroSolicitudes(db).RegistrarAsync(host.Empresa, central, host.Actor, "Terminal", Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow);
            var foreignCompany = Guid.NewGuid(); var foreignCentral = await TestCentrales.CreateAsync(db, foreignCompany);
            (foreign, _) = await new RegistroSolicitudes(db).RegistrarAsync(foreignCompany, foreignCentral, Guid.NewGuid(), "Ajena", Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow);
            driver = a.Id; otherDriver = b.Id;
        }
        using var anonymous = host.Factory.CreateClient();
        using (var denied = await Post(anonymous, $"/api/v1/despacho/solicitudes/{id}/ofertas", new { conductorId = driver }, Guid.NewGuid().ToString("N")))
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using (var denied = await Post(host.Client, $"/api/v1/despacho/solicitudes/{foreign}/ofertas", new { conductorId = driver }, Guid.NewGuid().ToString("N")))
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        using (var candidates = await host.Client.GetAsync("/api/v1/despacho/candidatos"))
        {
            Assert.Equal(HttpStatusCode.OK, candidates.StatusCode);
            using var data = JsonDocument.Parse(await candidates.Content.ReadAsStringAsync());
            Assert.All(data.RootElement.EnumerateArray(), x => Assert.False(x.GetProperty("ubicacionVerificada").GetBoolean()));
        }
        var path = $"/api/v1/despacho/solicitudes/{id}/ofertas";
        var key = Guid.NewGuid().ToString("N");
        using var first = await Post(host.Client, path, new { conductorId = driver }, key);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using var body = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        var intento = body.RootElement.GetProperty("intentoId").GetGuid();
        using (var replay = await Post(host.Client, path, new { conductorId = driver }, key))
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        using (var conflict = await Post(host.Client, path, new { conductorId = otherDriver }, key))
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        using (var missing = await Post(host.Client, path, new { conductorId = driver }, null))
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        using var driverClient = host.Factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var driverIdentity = await TestIdentity.SeedAsync(host.Factory, host.Empresa, RolesEmpresa.Conductor, driver);
        using var driverTokens = await TestIdentity.LoginAndroidAsync(driverClient, driverIdentity);
        using var accepted = await Post(driverClient, $"{path}/{intento}/aceptar", new { }, Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        using var acceptedBody = JsonDocument.Parse(await accepted.Content.ReadAsStringAsync());
        var viaje = acceptedBody.RootElement.GetProperty("viajeId").GetGuid();
        using var canceled = await Post(driverClient, $"/api/v1/despacho/solicitudes/{id}/viajes/{viaje}/cancelar-conductor", new { motivo = "No puede recoger" }, Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.OK, canceled.StatusCode);
        using var loaded = await host.Client.GetAsync($"/api/v1/solicitudes/{id}");
        using var read = JsonDocument.Parse(await loaded.Content.ReadAsStringAsync());
        Assert.Equal((int)EstadoSolicitud.Pendiente, read.RootElement.GetProperty("estado").GetInt32());
    }

    // Opt-in smoke regression, separate from the functional suite and from other DB load.
    [NeonFact]
    public async Task Http_RegistroFase4_MedicionRegresion()
    {
        if (Environment.GetEnvironmentVariable("RM_RUN_PHASE5_REGRESSION") != "1") return;
        var file = Environment.GetEnvironmentVariable("RM_PHASE5_OUTPUT") ?? throw new InvalidOperationException("Ruta externa obligatoria.");
        using var host = new TestingHost();
        await host.LoginAsync();
        async Task<double> Send(Guid central)
        {
            var timer = Stopwatch.StartNew();
            using var response = await Post(host.Client, "/api/v1/solicitudes", new { centralId = central, origen = "Prueba fase 5" }, Guid.NewGuid().ToString("N"));
            timer.Stop();
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return timer.Elapsed.TotalMilliseconds;
        }
        foreach (var concurrency in new[] { 1, 5 })
        {
            var warm = Guid.NewGuid();
            using (var setupScope = host.Factory.Services.CreateScope()) await TestCentrales.CreateAsync(setupScope.ServiceProvider.GetRequiredService<RadioMovilDbContext>(), host.Empresa, warm);
            await Task.WhenAll(Enumerable.Range(0, concurrency).Select(_ => Send(warm)));
            var central = Guid.NewGuid();
            using (var setupScope = host.Factory.Services.CreateScope()) await TestCentrales.CreateAsync(setupScope.ServiceProvider.GetRequiredService<RadioMovilDbContext>(), host.Empresa, central);
            using var gate = new SemaphoreSlim(concurrency);
            var values = await Task.WhenAll(Enumerable.Range(0, 100).Select(async _ =>
            {
                await gate.WaitAsync();
                try { return await Send(central); }
                finally { gate.Release(); }
            }));
            using var scope = host.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioMovilDbContext>();
            var rows = await db.Solicitudes.CountAsync(x => x.EmpresaId == host.Empresa && EF.Property<Guid>(x, "CentralId") == central);
            Assert.Equal(100, rows);
            var ordered = values.Order().ToArray();
            File.AppendAllText(file, JsonSerializer.Serialize(new { concurrency, confirmed = 100, persisted = rows,
                mean = values.Average(), p95 = ordered[94], p99 = ordered[98] }) + Environment.NewLine);
        }
    }
}
