using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RadioMovil.Domain;
using RadioMovil.Infrastructure;
using RADIOMOVIL;
using Xunit;

namespace RadioMovil.UnitTests;

public sealed class FaseBDominioTests
{
    [Theory]
    [InlineData("abc 123", "ABC123")]
    [InlineData(" 2345-xyz ", "2345-XYZ")]
    public void Patente_SeNormaliza(string entrada, string esperada) => Assert.Equal(esperada, Vehiculo.NormalizarPatente(entrada));

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("ABC_123")]
    [InlineData("ABCDEFGHIJKLMNOPQ")]
    public void Patente_InvalidaSeRechaza(string entrada) => Assert.Throws<ErrorDominio>(() => Vehiculo.NormalizarPatente(entrada));

    [Fact]
    public void Turno_CierreRegistraActorMotivoYNoReabre()
    {
        var actor = Guid.NewGuid();
        var inicio = DateTimeOffset.UtcNow;
        var turno = new Turno(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), inicio, actor);
        Assert.Equal(actor, turno.AbiertoPor);
        Assert.Throws<ErrorDominio>(() => turno.Cerrar(inicio.AddSeconds(-1), actor));
        Assert.Throws<ErrorDominio>(() => turno.Cerrar(inicio.AddMinutes(1), actor, new string('x', 501)));
        turno.Cerrar(inicio.AddMinutes(1), actor, " Fin de jornada ");
        Assert.Equal("Fin de jornada", turno.MotivoCierre);
        Assert.Equal(actor, turno.CerradoPor);
        Assert.Throws<ErrorDominio>(() => turno.Cerrar(inicio.AddMinutes(2), actor));
    }

    [Fact]
    public void Conductor_NombreLargoSeRechaza() =>
        Assert.Throws<ErrorDominio>(() => new Conductor(Guid.NewGuid(), new string('n', 161)));
}

public sealed class FaseBNeonTests
{
    private static WebApplicationFactory<Program> Factory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(x => x.UseEnvironment("Testing"));

    private static HttpClient Client(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object body, string? key = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString("N"));
        return await client.SendAsync(request);
    }

    private static async Task<Guid> IdAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> ConductorAsync(HttpClient client, Guid id)
    {
        using var json = JsonDocument.Parse(await client.GetStringAsync("/api/v1/conductores"));
        return json.RootElement.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == id).Clone();
    }

    [NeonFact]
    public async Task Api_AltasIdempotentesPermisosYAislamiento()
    {
        using var factory = Factory();
        var empresa = Guid.NewGuid(); var ajena = Guid.NewGuid();
        using var admin = Client(factory); using var operador = Client(factory); using var foreign = Client(factory);
        using var t1 = await TestIdentity.LoginAndroidAsync(admin, await TestIdentity.SeedAsync(factory, empresa, RolesEmpresa.Administrador));
        using var t2 = await TestIdentity.LoginAndroidAsync(operador, await TestIdentity.SeedAsync(factory, empresa));
        using var t3 = await TestIdentity.LoginAndroidAsync(foreign, await TestIdentity.SeedAsync(factory, ajena, RolesEmpresa.Administrador));

        var key = Guid.NewGuid().ToString("N");
        using var created = await PostAsync(admin, "/api/v1/conductores", new { nombre = "Conductor Fase B" }, key);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var conductor = await IdAsync(created);
        using var replay = await PostAsync(admin, "/api/v1/conductores", new { nombre = "Conductor Fase B" }, key);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(conductor, await IdAsync(replay));
        using var reused = await PostAsync(admin, "/api/v1/conductores", new { nombre = "Otro nombre" }, key);
        Assert.Equal(HttpStatusCode.Conflict, reused.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(operador, "/api/v1/conductores", new { nombre = "No permitido" })).StatusCode);
        using (var noKey = await admin.PostAsJsonAsync("/api/v1/conductores", new { nombre = "Sin clave" }))
            Assert.Equal(HttpStatusCode.BadRequest, noKey.StatusCode);

        var plate = "fb " + Guid.NewGuid().ToString("N")[..8];
        using var car = await PostAsync(admin, "/api/v1/vehiculos", new { patente = plate });
        Assert.Equal(HttpStatusCode.Created, car.StatusCode);
        var vehiculo = await IdAsync(car);
        using var duplicate = await PostAsync(admin, "/api/v1/vehiculos", new { patente = plate.Replace(" ", "").ToUpperInvariant() });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        using var badPlate = await PostAsync(admin, "/api/v1/vehiculos", new { patente = "##" });
        Assert.Equal(HttpStatusCode.BadRequest, badPlate.StatusCode);
        using (var list = JsonDocument.Parse(await operador.GetStringAsync("/api/v1/vehiculos")))
            Assert.Equal(plate.Replace(" ", "").ToUpperInvariant(), list.RootElement.EnumerateArray().Single().GetProperty("patente").GetString());

        // Empresa ajena: no ve ni puede usar recursos de otra empresa.
        using (var none = JsonDocument.Parse(await foreign.GetStringAsync("/api/v1/conductores"))) Assert.Empty(none.RootElement.EnumerateArray());
        using (var none = JsonDocument.Parse(await foreign.GetStringAsync("/api/v1/vehiculos"))) Assert.Empty(none.RootElement.EnumerateArray());
        using var foreignShift = await PostAsync(foreign, "/api/v1/turnos", new { conductorId = conductor, vehiculoId = vehiculo });
        Assert.Equal(HttpStatusCode.NotFound, foreignShift.StatusCode);
        Assert.Equal("FueraDeServicio", (await ConductorAsync(operador, conductor)).GetProperty("estado").GetString());
    }

    [NeonFact]
    public async Task Api_TurnoExclusivoEstadosHistorialYCierreBloqueadoPorOferta()
    {
        using var factory = Factory();
        var empresa = Guid.NewGuid(); var ajena = Guid.NewGuid();
        using var admin = Client(factory); using var operador = Client(factory); using var foreign = Client(factory);
        using var t1 = await TestIdentity.LoginAndroidAsync(admin, await TestIdentity.SeedAsync(factory, empresa, RolesEmpresa.Administrador));
        using var t2 = await TestIdentity.LoginAndroidAsync(operador, await TestIdentity.SeedAsync(factory, empresa));
        using var t3 = await TestIdentity.LoginAndroidAsync(foreign, await TestIdentity.SeedAsync(factory, ajena));
        var c1 = await IdAsync(await PostAsync(admin, "/api/v1/conductores", new { nombre = "Uno" }));
        var c2 = await IdAsync(await PostAsync(admin, "/api/v1/conductores", new { nombre = "Dos" }));
        var v1 = await IdAsync(await PostAsync(admin, "/api/v1/vehiculos", new { patente = "V1" + Guid.NewGuid().ToString("N")[..8] }));
        var v2 = await IdAsync(await PostAsync(admin, "/api/v1/vehiculos", new { patente = "V2" + Guid.NewGuid().ToString("N")[..8] }));

        var openKey = Guid.NewGuid().ToString("N");
        using var opened = await PostAsync(operador, "/api/v1/turnos", new { conductorId = c1, vehiculoId = v1 }, openKey);
        Assert.Equal(HttpStatusCode.Created, opened.StatusCode);
        var turno = await IdAsync(opened);
        using (var again = await PostAsync(operador, "/api/v1/turnos", new { conductorId = c1, vehiculoId = v1 }, openKey))
            Assert.Equal(turno, await IdAsync(again));
        Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(operador, "/api/v1/turnos", new { conductorId = c1, vehiculoId = v2 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(operador, "/api/v1/turnos", new { conductorId = c2, vehiculoId = v1 })).StatusCode);
        var state = await ConductorAsync(operador, c1);
        Assert.Equal("Disponible", state.GetProperty("estado").GetString());
        Assert.Equal(v1, state.GetProperty("turno").GetProperty("vehiculoId").GetGuid());
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(foreign, $"/api/v1/turnos/{turno}/cerrar", new { motivo = "ajeno" })).StatusCode);

        // Oferta activa: conductor Ocupado y cierre de turno rechazado.
        Guid solicitud, intento;
        using (var central = await admin.PostAsJsonAsync("/api/v1/centrales", new { nombre = "Central B " + Guid.NewGuid().ToString("N") }))
        {
            var centralId = await IdAsync(central);
            solicitud = await IdAsync(await PostAsync(operador, "/api/v1/solicitudes", new { centralId, origen = "Origen Fase B" }));
            using var offer = await PostAsync(operador, $"/api/v1/despacho/solicitudes/{solicitud}/ofertas", new { conductorId = c1 });
            Assert.Equal(HttpStatusCode.Created, offer.StatusCode);
            using var offerJson = JsonDocument.Parse(await offer.Content.ReadAsStringAsync());
            intento = offerJson.RootElement.GetProperty("intentoId").GetGuid();
        }
        state = await ConductorAsync(operador, c1);
        Assert.Equal("Ocupado", state.GetProperty("estado").GetString());
        Assert.Equal("Oferta", state.GetProperty("ocupacion").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(operador, $"/api/v1/turnos/{turno}/cerrar", new { motivo = "Fin" })).StatusCode);

        // La oferta vence por el flujo real del motor (20 s); después el cierre procede.
        await Task.Delay(TimeSpan.FromSeconds(21));
        using (var expired = await PostAsync(operador, $"/api/v1/despacho/solicitudes/{solicitud}/ofertas/{intento}/vencer", new { }))
            Assert.Equal(HttpStatusCode.OK, expired.StatusCode);
        var closeKey = Guid.NewGuid().ToString("N");
        using var closed = await PostAsync(operador, $"/api/v1/turnos/{turno}/cerrar", new { motivo = "Fin de jornada" }, closeKey);
        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        using (var replay = await PostAsync(operador, $"/api/v1/turnos/{turno}/cerrar", new { motivo = "Fin de jornada" }, closeKey))
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(operador, $"/api/v1/turnos/{turno}/cerrar", new { motivo = "Otra vez" })).StatusCode);
        Assert.Equal("FueraDeServicio", (await ConductorAsync(operador, c1)).GetProperty("estado").GetString());
        using (var history = JsonDocument.Parse(await operador.GetStringAsync($"/api/v1/turnos?abiertos=false&conductorId={c1}")))
        {
            var item = history.RootElement.GetProperty("items").EnumerateArray().Single();
            Assert.Equal("Fin de jornada", item.GetProperty("motivoCierre").GetString());
            Assert.NotEqual(JsonValueKind.Null, item.GetProperty("fin").ValueKind);
        }
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioMovilDbContext>();
            var saved = await db.Turnos.AsNoTracking().SingleAsync(x => x.Id == turno);
            Assert.NotNull(saved.AbiertoPor); Assert.NotNull(saved.CerradoPor);
            Assert.Equal(2, await db.AuditoriaAcceso.CountAsync(a => a.EmpresaId == empresa && a.ObjetoId == turno && (a.Tipo == "TurnoAbierto" || a.Tipo == "TurnoCerrado")));
        }
        // Después del cierre el vehículo vuelve a estar libre para otro conductor.
        Assert.Equal(HttpStatusCode.Created, (await PostAsync(operador, "/api/v1/turnos", new { conductorId = c2, vehiculoId = v1 })).StatusCode);
    }

    [NeonFact]
    public async Task Concurrencia_DosAperturasMismoConductor_UnaGana()
    {
        using var factory = Factory();
        var empresa = Guid.NewGuid();
        using var admin = Client(factory);
        using var t = await TestIdentity.LoginAndroidAsync(admin, await TestIdentity.SeedAsync(factory, empresa, RolesEmpresa.Administrador));
        var c = await IdAsync(await PostAsync(admin, "/api/v1/conductores", new { nombre = "Concurrente" }));
        var va = await IdAsync(await PostAsync(admin, "/api/v1/vehiculos", new { patente = "CA" + Guid.NewGuid().ToString("N")[..8] }));
        var vb = await IdAsync(await PostAsync(admin, "/api/v1/vehiculos", new { patente = "CB" + Guid.NewGuid().ToString("N")[..8] }));
        var results = await Task.WhenAll(
            PostAsync(admin, "/api/v1/turnos", new { conductorId = c, vehiculoId = va }),
            PostAsync(admin, "/api/v1/turnos", new { conductorId = c, vehiculoId = vb }));
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.Created);
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
        foreach (var r in results) r.Dispose();
    }

    [NeonFact]
    public async Task BaseDeDatos_OfertaSinTurnoAbiertoSeRechaza()
    {
        var connection = NeonTestingGuard.RequireTesting(Environment.GetEnvironmentVariable("NEON_TESTING_DIRECT_CONNECTION_STRING"), requireDirect: true);
        await using var db = new RadioMovilDbContext(new DbContextOptionsBuilder<RadioMovilDbContext>()
            .UseNpgsql(connection, x => x.UseNetTopologySuite()).Options);
        var empresa = Guid.NewGuid();
        var conductor = new Conductor(empresa, "Sin turno");
        db.Conductores.Add(conductor);
        await db.SaveChangesAsync();
        var central = await TestCentrales.CreateAsync(db, empresa);
        var (solicitud, _) = await new RegistroSolicitudes(db).RegistrarAsync(empresa, central, Guid.NewGuid(), "Origen", Guid.NewGuid().ToString(), DateTimeOffset.UtcNow);
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO intentos_despacho ("Id","EmpresaId","SolicitudId","ConductorId","CreadoEn","VenceEn","Estado")
            VALUES ({Guid.NewGuid()},{empresa},{solicitud},{conductor.Id},{DateTimeOffset.UtcNow},{DateTimeOffset.UtcNow.AddMinutes(1)},{"Ofertado"})
            """));
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
    }
}
