using Microsoft.EntityFrameworkCore;
using RadioMovil.Infrastructure;
using Xunit;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RADIOMOVIL;

namespace RadioMovil.UnitTests;

public sealed class FaseANeonTests
{
    [NeonFact]
    public async Task Api_CentralRealRegistroConsultaYRechazoEntreEmpresas()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(x => x.UseEnvironment("Testing"));
        using var admin = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var empresa = Guid.NewGuid(); var ajena = Guid.NewGuid();
        var user = await TestIdentity.SeedAsync(factory, empresa, RolesEmpresa.Administrador);
        var other = await TestIdentity.SeedAsync(factory, ajena);
        using var adminToken = await TestIdentity.LoginAndroidAsync(admin, user);
        using var create = await admin.PostAsJsonAsync("/api/v1/centrales", new { nombre = "Central Operativa" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var centralJson = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var central = centralJson.RootElement.GetProperty("id").GetGuid();
        using var list = JsonDocument.Parse(await admin.GetStringAsync("/api/v1/centrales"));
        Assert.Contains(list.RootElement.EnumerateArray(), x => x.GetProperty("id").GetGuid() == central);
        using var foreignClient = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var foreignToken = await TestIdentity.LoginAndroidAsync(foreignClient, other);
        using var foreignList = JsonDocument.Parse(await foreignClient.GetStringAsync("/api/v1/centrales"));
        Assert.Empty(foreignList.RootElement.EnumerateArray());
        var payload = new { centralId = central, origen = "Calle 12", telefonoPasajero = "123", nombrePasajero = "Paciente", destino = "Hospital", observaciones = "Puerta 2", latitud = -16.5, longitud = -68.15 };
        var key = Guid.NewGuid().ToString("N");
        admin.DefaultRequestHeaders.Add("Idempotency-Key", key);
        using var first = await admin.PostAsJsonAsync("/api/v1/solicitudes", payload);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using var firstJson = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        var id = firstJson.RootElement.GetProperty("id").GetGuid();
        using var replay = await admin.PostAsJsonAsync("/api/v1/solicitudes", payload);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        using var byId = JsonDocument.Parse(await admin.GetStringAsync($"/api/v1/solicitudes/{id}"));
        Assert.Equal("Paciente", byId.RootElement.GetProperty("nombrePasajero").GetString());
        Assert.Equal(-16.5, byId.RootElement.GetProperty("latitud").GetDouble());
        using var rejected = await foreignClient.PostAsJsonAsync("/api/v1/solicitudes", payload);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await foreignClient.GetAsync($"/api/v1/solicitudes/{id}")).StatusCode);
    }

    [NeonFact]
    public async Task Registro_ExigeCentralEmpresaYConservaDatosEIdempotencia()
    {
        var connection = NeonTestingGuard.RequireTesting(Environment.GetEnvironmentVariable("NEON_TESTING_DIRECT_CONNECTION_STRING"), requireDirect: true);
        RadioMovilDbContext Open() => new(new DbContextOptionsBuilder<RadioMovilDbContext>()
            .UseNpgsql(connection, x => x.UseNetTopologySuite()).Options);
        var empresa = Guid.NewGuid(); var otra = Guid.NewGuid(); var actor = Guid.NewGuid();
        var central = new Central { Id = Guid.NewGuid(), EmpresaId = empresa, Nombre = "Central Fase A " + Guid.NewGuid().ToString("N") };
        await using (var db = Open()) { db.Centrales.Add(central); await db.SaveChangesAsync(); }
        var datos = new DatosRegistro("12345", "Persona prueba", "Destino prueba", "Frente a puerta", -16.5, -68.15);
        var key = Guid.NewGuid().ToString("N");
        await using (var db = Open())
        {
            var registro = new RegistroSolicitudes(db);
            await Assert.ThrowsAsync<CentralNoAutorizadaException>(() => registro.RegistrarConResultadoAsync(otra, central.Id, actor, "Origen", key, DateTimeOffset.UtcNow, datos: datos));
            await Assert.ThrowsAsync<CentralNoAutorizadaException>(() => registro.RegistrarConResultadoAsync(empresa, Guid.NewGuid(), actor, "Origen", key, DateTimeOffset.UtcNow, datos: datos));
            var result = await registro.RegistrarConResultadoAsync(empresa, central.Id, actor, "Origen", key, DateTimeOffset.UtcNow, datos: datos);
            Assert.True(result.creada);
            var replay = await registro.RegistrarConResultadoAsync(empresa, central.Id, actor, "Origen", key, DateTimeOffset.UtcNow, datos: datos);
            Assert.False(replay.creada);
            Assert.Equal(result.solicitudId, replay.solicitudId);
            await Assert.ThrowsAsync<ConflictoIdempotenciaException>(() => registro.RegistrarConResultadoAsync(empresa, central.Id, actor, "Origen", key, DateTimeOffset.UtcNow, datos: datos with { Destino = "Otro" }));
            var request = await db.Solicitudes.SingleAsync(x => x.Id == result.solicitudId);
            Assert.Equal("12345", db.Entry(request).Property<string?>("TelefonoPasajero").CurrentValue);
            Assert.Equal(-68.15, db.Entry(request).Property<NetTopologySuite.Geometries.Point?>("PuntoRecogida").CurrentValue?.X);
        }
    }
}
