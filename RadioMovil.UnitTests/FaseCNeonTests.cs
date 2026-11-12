using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using RadioMovil.Infrastructure;
using RADIOMOVIL;
using Xunit;

namespace RadioMovil.UnitTests;

public sealed class FaseCGpsDominioTests
{
    [Fact]
    public void Clasificacion_UsaUmbralesDeAgents()
    {
        var o = new GpsOptions();
        var ahora = DateTimeOffset.UtcNow;
        Assert.Equal(EstadoGps.SinSenal, RecepcionGps.Clasificar(null, ahora, o));
        Assert.Equal(EstadoGps.Reciente, RecepcionGps.Clasificar(ahora.AddSeconds(-30), ahora, o));
        Assert.Equal(EstadoGps.Desactualizado, RecepcionGps.Clasificar(ahora.AddSeconds(-31), ahora, o));
        Assert.Equal(EstadoGps.Desactualizado, RecepcionGps.Clasificar(ahora.AddSeconds(-90), ahora, o));
        Assert.Equal(EstadoGps.SinSenal, RecepcionGps.Clasificar(ahora.AddSeconds(-91), ahora, o));
    }
}

public sealed class FaseCNeonTests
{
    private static HttpClient Client(WebApplicationFactory<Program> f) => f.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

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

    private static Task<HttpResponseMessage> GpsAsync(HttpClient client, double lat, double lng, DateTimeOffset capturada, double precision = 8) =>
        client.PostAsJsonAsync("/api/v1/conductor/ubicaciones", new { latitud = lat, longitud = lng, precisionMetros = precision, capturadaEn = capturada });

    private static async Task<JsonElement> JsonAsync(HttpClient client, string path)
    {
        using var json = JsonDocument.Parse(await client.GetStringAsync(path));
        return json.RootElement.Clone();
    }

    [NeonFact]
    public async Task Conductor_TurnoAutorizado_GpsReal_YVistaDeLaCentral()
    {
        // Contra Neon cada petición tarda cientos de ms: un intervalo mayor hace determinista la prueba de frecuencia.
        Environment.SetEnvironmentVariable("Gps__IntervaloMinimoSegundos", "15");
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(x => x.UseEnvironment("Testing"));
        var empresa = Guid.NewGuid(); var ajena = Guid.NewGuid();
        using var admin = Client(factory); using var operador = Client(factory); using var foreign = Client(factory);
        using var t1 = await TestIdentity.LoginAndroidAsync(admin, await TestIdentity.SeedAsync(factory, empresa, RolesEmpresa.Administrador));
        using var t2 = await TestIdentity.LoginAndroidAsync(operador, await TestIdentity.SeedAsync(factory, empresa));
        using var t3 = await TestIdentity.LoginAndroidAsync(foreign, await TestIdentity.SeedAsync(factory, ajena));

        var conductor = await IdAsync(await PostAsync(admin, "/api/v1/conductores", new { nombre = "Conductor GPS" }));
        var autorizado = await IdAsync(await PostAsync(admin, "/api/v1/vehiculos", new { patente = "GA" + Guid.NewGuid().ToString("N")[..8] }));
        var noAutorizado = await IdAsync(await PostAsync(admin, "/api/v1/vehiculos", new { patente = "GN" + Guid.NewGuid().ToString("N")[..8] }));
        using (var auth = await PostAsync(admin, "/api/v1/autorizaciones-vehiculo", new { conductorId = conductor, vehiculoId = autorizado }))
            Assert.Equal(HttpStatusCode.Created, auth.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(operador, "/api/v1/autorizaciones-vehiculo", new { conductorId = conductor, vehiculoId = noAutorizado })).StatusCode);

        // Acceso del conductor creado por el administrador con el endpoint existente.
        var email = $"driver-{Guid.NewGuid():N}@example.invalid"; var password = "Strong!" + Guid.NewGuid().ToString("N") + "8";
        using (var access = await admin.PostAsJsonAsync("/api/v1/auth/usuarios", new { email, password, rol = "Conductor", conductorId = conductor }))
            Assert.Equal(HttpStatusCode.Created, access.StatusCode);
        using (var again = await admin.PostAsJsonAsync("/api/v1/auth/usuarios", new { email = "x" + email, password, rol = "Conductor", conductorId = conductor }))
            Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        using var driver = Client(factory);
        using var t4 = await TestIdentity.LoginAndroidAsync(driver, new TestIdentity.Identity(Guid.Empty, email, password, empresa));

        var yo = await JsonAsync(driver, "/api/v1/conductor/yo");
        Assert.Equal("FueraDeServicio", yo.GetProperty("estado").GetString());
        Assert.Equal("SinSenal", yo.GetProperty("estadoGps").GetString());
        var vehiculos = await JsonAsync(driver, "/api/v1/conductor/vehiculos");
        Assert.Equal(autorizado, Assert.Single(vehiculos.EnumerateArray()).GetProperty("vehiculoId").GetGuid());
        Assert.Equal(HttpStatusCode.Forbidden, (await operador.GetAsync("/api/v1/conductor/yo")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await GpsAsync(driver, -16.5, -68.15, DateTimeOffset.UtcNow)).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(driver, "/api/v1/conductor/turno", new { vehiculoId = noAutorizado })).StatusCode);
        var openKey = Guid.NewGuid().ToString("N");
        using (var opened = await PostAsync(driver, "/api/v1/conductor/turno", new { vehiculoId = autorizado }, openKey))
            Assert.Equal(HttpStatusCode.Created, opened.StatusCode);
        using (var replay = await PostAsync(driver, "/api/v1/conductor/turno", new { vehiculoId = autorizado }, openKey))
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);

        // Validación de coordenadas y tiempos.
        Assert.Equal(HttpStatusCode.BadRequest, (await GpsAsync(driver, 91, 0, DateTimeOffset.UtcNow)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await GpsAsync(driver, -16.5, -68.15, DateTimeOffset.UtcNow, precision: 0)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await GpsAsync(driver, -16.5, -68.15, DateTimeOffset.UtcNow.AddMinutes(5))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await GpsAsync(driver, -16.5, -68.15, DateTimeOffset.UtcNow.AddMinutes(-10))).StatusCode);

        var capturada = DateTimeOffset.UtcNow;
        using (var first = await GpsAsync(driver, -16.5001, -68.1501, capturada, 6.5))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            using var json = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
            Assert.True(json.RootElement.GetProperty("aceptada").GetBoolean());
            Assert.Equal("Reciente", json.RootElement.GetProperty("estadoGps").GetString());
        }
        using (var repeated = await GpsAsync(driver, -16.5001, -68.1501, capturada, 6.5))
        {
            using var json = JsonDocument.Parse(await repeated.Content.ReadAsStringAsync());
            Assert.False(json.RootElement.GetProperty("aceptada").GetBoolean());
            Assert.Equal("Repetida", json.RootElement.GetProperty("motivo").GetString());
        }
        using (var tooFast = await GpsAsync(driver, -16.5002, -68.1502, capturada.AddSeconds(1)))
        {
            Assert.Equal((HttpStatusCode)429, tooFast.StatusCode);
            Assert.True(tooFast.Headers.RetryAfter is not null);
        }
        await Task.Delay(TimeSpan.FromSeconds(15.5));
        using (var second = await GpsAsync(driver, -16.5003, -68.1503, DateTimeOffset.UtcNow, 5))
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        // La central ve la última ubicación real; empresa ajena no ve nada.
        var ubicaciones = await JsonAsync(operador, "/api/v1/ubicaciones");
        var item = Assert.Single(ubicaciones.GetProperty("items").EnumerateArray());
        Assert.Equal(conductor, item.GetProperty("conductorId").GetGuid());
        Assert.Equal(-16.5003, item.GetProperty("latitud").GetDouble(), 6);
        Assert.Equal(5, item.GetProperty("precisionMetros").GetDouble());
        Assert.Equal("Reciente", item.GetProperty("estadoGps").GetString());
        Assert.Empty((await JsonAsync(foreign, "/api/v1/ubicaciones")).GetProperty("items").EnumerateArray());
        var candidato = Assert.Single((await JsonAsync(operador, "/api/v1/despacho/candidatos")).EnumerateArray());
        Assert.True(candidato.GetProperty("ubicacionVerificada").GetBoolean());
        Assert.Equal("Reciente", (await JsonAsync(driver, "/api/v1/conductor/yo")).GetProperty("estadoGps").GetString());

        // Historial conservado; cerrar turno detiene la recepción y la vista.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioMovilDbContext>();
            Assert.Equal(2, await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(db.UbicacionesHistorial, h => h.ConductorId == conductor));
        }
        using (var closed = await PostAsync(driver, "/api/v1/conductor/turno/cerrar", new { motivo = "Fin de prueba" }))
            Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await GpsAsync(driver, -16.5, -68.15, DateTimeOffset.UtcNow)).StatusCode);
        Assert.Empty((await JsonAsync(operador, "/api/v1/ubicaciones")).GetProperty("items").EnumerateArray());

        // Revocar impide abrir un turno nuevo con ese vehículo.
        var autorizacion = (await JsonAsync(admin, $"/api/v1/autorizaciones-vehiculo?conductorId={conductor}")).EnumerateArray().Single().GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(admin, $"/api/v1/autorizaciones-vehiculo/{autorizacion}/revocar", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(driver, "/api/v1/conductor/turno", new { vehiculoId = autorizado })).StatusCode);
        Assert.Empty((await JsonAsync(driver, "/api/v1/conductor/vehiculos")).EnumerateArray());
    }
}
