using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RadioMovil.Domain;
using RadioMovil.Infrastructure;
using RADIOMOVIL;
using Xunit;

namespace RadioMovil.UnitTests;

public sealed class AsignacionDirectaDominioTests
{
    [Fact]
    public void AsignarDirecto_CreaViajeSinOfertaNiAceptacion()
    {
        var actor = Guid.NewGuid(); var conductor = Guid.NewGuid(); var t0 = DateTimeOffset.UtcNow;
        var solicitud = new Solicitud(Guid.NewGuid(), "Plaza", t0, actor);
        var viaje = solicitud.AsignarDirecto(conductor, t0.AddSeconds(5), actor);
        Assert.Empty(solicitud.Intentos);
        Assert.DoesNotContain(solicitud.Eventos, e => e.Tipo.StartsWith("Oferta", StringComparison.Ordinal));
        var evento = Assert.Single(solicitud.Eventos, e => e.Tipo == "AsignadaDirecta");
        Assert.Equal(actor, evento.ActorId); Assert.Equal(conductor.ToString(), evento.Motivo);
        Assert.Equal(EstadoSolicitud.Asignada, solicitud.Estado);
        Assert.Equal(viaje.Id, solicitud.ViajeId);
        Assert.Equal(EstadoViaje.Asignado, viaje.Estado);
        Assert.Throws<ErrorDominio>(() => solicitud.AsignarDirecto(Guid.NewGuid(), t0.AddSeconds(6), actor));
    }

    [Fact]
    public void AsignarDirecto_SoloDesdePendiente()
    {
        var actor = Guid.NewGuid(); var t0 = DateTimeOffset.UtcNow;
        var ofertada = new Solicitud(Guid.NewGuid(), "Plaza", t0, actor);
        ofertada.Ofrecer(Guid.NewGuid(), t0, t0.AddSeconds(20), actor);
        Assert.Throws<ErrorDominio>(() => ofertada.AsignarDirecto(Guid.NewGuid(), t0.AddSeconds(1), actor));
        var cancelada = new Solicitud(Guid.NewGuid(), "Plaza", t0, actor);
        cancelada.CancelarCliente(t0.AddSeconds(1), actor, "Cliente desiste");
        Assert.Throws<ErrorDominio>(() => cancelada.AsignarDirecto(Guid.NewGuid(), t0.AddSeconds(2), actor));
    }

    [Fact]
    public void ConfirmarRecepcion_EsEntregaNoAceptacion()
    {
        var t0 = DateTimeOffset.UtcNow;
        var viaje = new Viaje(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), t0);
        Assert.Throws<ErrorDominio>(() => viaje.ConfirmarRecepcion(t0.AddSeconds(-1)));
        Assert.True(viaje.ConfirmarRecepcion(t0.AddSeconds(3)));
        Assert.False(viaje.ConfirmarRecepcion(t0.AddSeconds(9)));
        Assert.Equal(t0.AddSeconds(3), viaje.RecibidaEn);
        Assert.Equal(EstadoViaje.Asignado, viaje.Estado);
    }
}

public sealed class PasosViajeDominioTests
{
    private static Viaje Asignado(out DateTimeOffset t0)
    {
        t0 = DateTimeOffset.UtcNow;
        return new Solicitud(Guid.NewGuid(), "Plaza", t0, Guid.NewGuid()).AsignarDirecto(Guid.NewGuid(), t0, Guid.NewGuid());
    }

    [Fact]
    public void CuatroPasos_EnOrden_YLlegadaOpcional()
    {
        var viaje = Asignado(out var t0);
        Assert.Throws<ErrorDominio>(() => viaje.IniciarRecorrido(t0));
        Assert.Throws<ErrorDominio>(() => viaje.MarcarLlegada(t0));
        Assert.Throws<ErrorDominio>(() => viaje.Finalizar(t0));
        viaje.IrARecoger(t0.AddSeconds(1)); Assert.Equal(EstadoViaje.EnCamino, viaje.Estado);
        viaje.MarcarLlegada(t0.AddSeconds(2)); Assert.Equal(EstadoViaje.EnPuerta, viaje.Estado);
        viaje.IniciarRecorrido(t0.AddSeconds(3)); Assert.Equal(EstadoViaje.EnCurso, viaje.Estado);
        Assert.True(viaje.EstaActivo);
        viaje.Finalizar(t0.AddSeconds(4)); Assert.Equal(EstadoViaje.Finalizado, viaje.Estado);
        Assert.False(viaje.EstaActivo);
        Assert.Equal("La carrera ya está cerrada.", Assert.Throws<ErrorDominio>(() => viaje.IrARecoger(t0.AddSeconds(5))).Message);

        var sinLlegada = Asignado(out var t1);
        sinLlegada.IrARecoger(t1); sinLlegada.IniciarRecorrido(t1);
        Assert.Equal(EstadoViaje.EnCurso, sinLlegada.Estado);
        Assert.Throws<ErrorDominio>(() => Asignado(out var t2).IrARecoger(t2.AddSeconds(-1)));
    }

    [Fact]
    public void Central_PuedeCancelarAntesDeRecoger_PeroNoConPasajeroABordo()
    {
        var actor = Guid.NewGuid(); var t0 = DateTimeOffset.UtcNow;
        var solicitud = new Solicitud(Guid.NewGuid(), "Plaza", t0, actor);
        var viaje = solicitud.AsignarDirecto(Guid.NewGuid(), t0, actor);
        viaje.IrARecoger(t0.AddSeconds(1)); viaje.MarcarLlegada(t0.AddSeconds(2));
        solicitud.CancelarCliente(viaje, t0.AddSeconds(3), actor, "Cliente desiste");
        Assert.Equal(EstadoViaje.Incidencia, viaje.Estado);

        var otra = new Solicitud(Guid.NewGuid(), "Plaza", t0, actor);
        var enCurso = otra.AsignarDirecto(Guid.NewGuid(), t0, actor);
        enCurso.IrARecoger(t0); enCurso.IniciarRecorrido(t0); otra.IniciarViaje(enCurso.Id, t0, actor);
        Assert.Throws<ErrorDominio>(() => otra.CancelarCliente(enCurso, t0.AddSeconds(1), actor, "Tarde"));
    }
}

public sealed class AsignacionDirectaNeonTests
{
    private static HttpClient Client(WebApplicationFactory<Program> f) => f.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object body, string? key = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString("N"));
        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    private static async Task<JsonElement> GetAsync(HttpClient client, string path)
    {
        using var json = JsonDocument.Parse(await client.GetStringAsync(path));
        return json.RootElement.Clone();
    }

    private sealed record Flota(WebApplicationFactory<Program> Factory, Guid Empresa, HttpClient Admin, HttpClient Operador, Guid Central);

    private static async Task<(Flota flota, IDisposable[] sesiones)> FlotaAsync(WebApplicationFactory<Program> factory)
    {
        var empresa = Guid.NewGuid();
        var admin = Client(factory); var operador = Client(factory);
        var s1 = await TestIdentity.LoginAndroidAsync(admin, await TestIdentity.SeedAsync(factory, empresa, RolesEmpresa.Administrador));
        var s2 = await TestIdentity.LoginAndroidAsync(operador, await TestIdentity.SeedAsync(factory, empresa));
        using var central = await admin.PostAsJsonAsync("/api/v1/centrales", new { nombre = "Central " + Guid.NewGuid().ToString("N")[..6] });
        Assert.Equal(HttpStatusCode.Created, central.StatusCode);
        return (new Flota(factory, empresa, admin, operador, (await BodyAsync(central)).GetProperty("id").GetGuid()), [s1, s2, admin, operador]);
    }

    /// <summary>Conductor con vehículo y turno abierto por la central; con acceso propio si se pide (para GPS y app).</summary>
    private static async Task<(Guid conductor, HttpClient? app, IDisposable? sesion)> ConductorEnTurnoAsync(Flota f, string nombre, bool conAcceso)
    {
        var conductor = (await BodyAsync(await PostAsync(f.Admin, "/api/v1/conductores", new { nombre }))).GetProperty("id").GetGuid();
        var vehiculo = (await BodyAsync(await PostAsync(f.Admin, "/api/v1/vehiculos", new { patente = "AD" + Guid.NewGuid().ToString("N")[..8] }))).GetProperty("id").GetGuid();
        using (var turno = await PostAsync(f.Operador, "/api/v1/turnos", new { conductorId = conductor, vehiculoId = vehiculo }))
            Assert.Equal(HttpStatusCode.Created, turno.StatusCode);
        if (!conAcceso) return (conductor, null, null);
        var email = $"asig-{Guid.NewGuid():N}@example.invalid"; var password = "Strong!" + Guid.NewGuid().ToString("N") + "8";
        using (var access = await f.Admin.PostAsJsonAsync("/api/v1/auth/usuarios", new { email, password, rol = "Conductor", conductorId = conductor }))
            Assert.Equal(HttpStatusCode.Created, access.StatusCode);
        var app = Client(f.Factory);
        var sesion = await TestIdentity.LoginAndroidAsync(app, new TestIdentity.Identity(Guid.Empty, email, password, f.Empresa));
        return (conductor, app, sesion);
    }

    private static async Task<Guid> SolicitudAsync(Flota f, double? lat = null, double? lng = null, double? destLat = null, double? destLng = null)
    {
        using var created = await PostAsync(f.Operador, "/api/v1/solicitudes", new
        {
            centralId = f.Central, origen = "Av. Arce y Pinilla", destino = "Terminal de buses", nombrePasajero = "Pasajero de prueba",
            latitud = lat, longitud = lng, destinoLatitud = destLat, destinoLongitud = destLng
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await BodyAsync(created);
        Assert.Equal(destLat, body.GetProperty("destinoLatitud").ValueKind == JsonValueKind.Null ? null : body.GetProperty("destinoLatitud").GetDouble());
        return body.GetProperty("id").GetGuid();
    }

    [NeonFact]
    public async Task Central_AsignaDirecto_ConductorRecibeOrdenYRecupera()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(x => x.UseEnvironment("Testing"));
        var (f, sesiones) = await FlotaAsync(factory);
        try
        {
            var (cerca, appCerca, s3) = await ConductorEnTurnoAsync(f, "Conductor cercano", conAcceso: true);
            var (lejos, appLejos, s4) = await ConductorEnTurnoAsync(f, "Conductor lejano", conAcceso: true);
            var (sinGps, _, _) = await ConductorEnTurnoAsync(f, "Conductor sin GPS", conAcceso: false);
            using var _s3 = s3; using var _s4 = s4; using var _a1 = appCerca!; using var _a2 = appLejos!;
            Assert.NotNull(appCerca); Assert.NotNull(appLejos);
            // Lecturas sintéticas de prueba enviadas por el contrato real del conductor (no son posiciones operativas).
            Assert.Equal(HttpStatusCode.OK, (await appCerca!.PostAsJsonAsync("/api/v1/conductor/ubicaciones", new { latitud = -16.5005, longitud = -68.1300, precisionMetros = 8, capturadaEn = DateTimeOffset.UtcNow })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await appLejos!.PostAsJsonAsync("/api/v1/conductor/ubicaciones", new { latitud = -16.5400, longitud = -68.0800, precisionMetros = 8, capturadaEn = DateTimeOffset.UtcNow })).StatusCode);

            var solicitud = await SolicitudAsync(f, -16.5000, -68.1300, -16.4900, -68.1500);

            // Cercanos: GPS reciente por distancia geodésica; sin GPS al final, sin posición ni distancia inventadas.
            var candidatos = await GetAsync(f.Operador, $"/api/v1/despacho/solicitudes/{solicitud}/candidatos");
            Assert.Equal(-16.5, candidatos.GetProperty("recogidaLatitud").GetDouble(), 6);
            var items = candidatos.GetProperty("items").EnumerateArray().ToList();
            Assert.Equal([cerca, lejos, sinGps], items.Select(i => i.GetProperty("conductorId").GetGuid()));
            Assert.InRange(items[0].GetProperty("distanciaMetros").GetDouble(), 40, 70);
            Assert.True(items[1].GetProperty("distanciaMetros").GetDouble() > 5000);
            Assert.Equal("SinSenal", items[2].GetProperty("estadoGps").GetString());
            Assert.Equal(JsonValueKind.Null, items[2].GetProperty("distanciaMetros").ValueKind);
            Assert.Equal(JsonValueKind.Null, items[2].GetProperty("latitud").ValueKind);

            // Solo la central asigna: el conductor no puede asignarse ni aceptar nada.
            Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(appCerca, $"/api/v1/despacho/solicitudes/{solicitud}/asignar", new { conductorId = cerca })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await f.Operador.PostAsJsonAsync($"/api/v1/despacho/solicitudes/{solicitud}/asignar", new { conductorId = cerca })).StatusCode);

            var key = Guid.NewGuid().ToString("N");
            using var asignada = await PostAsync(f.Operador, $"/api/v1/despacho/solicitudes/{solicitud}/asignar", new { conductorId = cerca }, key);
            Assert.Equal(HttpStatusCode.Created, asignada.StatusCode);
            var resultado = await BodyAsync(asignada);
            var viaje = resultado.GetProperty("viajeId").GetGuid();
            // Contrato existente de despacho: estado numérico (mismo formato que ofertas/cancelaciones).
            Assert.Equal((int)EstadoSolicitud.Asignada, resultado.GetProperty("estado").GetInt32());
            Assert.Equal(JsonValueKind.Null, resultado.GetProperty("intentoId").ValueKind);
            using (var replay = await PostAsync(f.Operador, $"/api/v1/despacho/solicitudes/{solicitud}/asignar", new { conductorId = cerca }, key))
            {
                Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
                Assert.Equal(viaje, (await BodyAsync(replay)).GetProperty("viajeId").GetGuid());
            }
            Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(f.Operador, $"/api/v1/despacho/solicitudes/{solicitud}/asignar", new { conductorId = lejos }, key)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(f.Operador, $"/api/v1/despacho/solicitudes/{solicitud}/asignar", new { conductorId = lejos })).StatusCode);

            // Conductor ocupado: no aparece como candidato y no admite otra carrera.
            var otra = await SolicitudAsync(f);
            Assert.DoesNotContain((await GetAsync(f.Operador, $"/api/v1/despacho/solicitudes/{otra}/candidatos")).GetProperty("items").EnumerateArray(),
                i => i.GetProperty("conductorId").GetGuid() == cerca);
            Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(f.Operador, $"/api/v1/despacho/solicitudes/{otra}/asignar", new { conductorId = cerca })).StatusCode);

            // El conductor recupera su orden (al abrir/reconectar) con recogida y destino; recepción ≠ asignación.
            var yo = await GetAsync(appCerca, "/api/v1/conductor/yo");
            Assert.Equal(viaje, yo.GetProperty("carreraActivaId").GetGuid());
            Assert.Equal("Viaje", yo.GetProperty("ocupacion").GetString());
            Assert.Equal(JsonValueKind.Null, (await GetAsync(appLejos, "/api/v1/conductor/yo")).GetProperty("carreraActivaId").ValueKind);
            var carrera = Assert.Single((await GetAsync(appCerca, "/api/v1/conductor/carreras")).EnumerateArray());
            Assert.Equal(viaje, carrera.GetProperty("viajeId").GetGuid());
            Assert.Equal(-16.5, carrera.GetProperty("recogidaLatitud").GetDouble(), 6);
            Assert.Equal(-68.15, carrera.GetProperty("destinoLongitud").GetDouble(), 6);
            Assert.Equal("Terminal de buses", carrera.GetProperty("destino").GetString());
            Assert.Equal(JsonValueKind.Null, carrera.GetProperty("recibidaEn").ValueKind);
            Assert.Empty((await GetAsync(appLejos, "/api/v1/conductor/carreras")).EnumerateArray());
            Assert.Equal(HttpStatusCode.NotFound, (await appLejos.PostAsync($"/api/v1/conductor/carreras/{viaje}/recibida", null)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await f.Operador.PostAsync($"/api/v1/conductor/carreras/{viaje}/recibida", null)).StatusCode);
            var primera = await BodyAsync(await appCerca.PostAsync($"/api/v1/conductor/carreras/{viaje}/recibida", null));
            Assert.False(primera.GetProperty("repetida").GetBoolean());
            var segunda = await BodyAsync(await appCerca.PostAsync($"/api/v1/conductor/carreras/{viaje}/recibida", null));
            Assert.True(segunda.GetProperty("repetida").GetBoolean());
            Assert.Equal(primera.GetProperty("recibidaEn").GetDateTimeOffset(), segunda.GetProperty("recibidaEn").GetDateTimeOffset());

            // La central ve la carrera activa y su entrega; el turno no puede cerrarse con la carrera activa.
            var activa = Assert.Single((await GetAsync(f.Operador, "/api/v1/despacho/carreras")).EnumerateArray());
            Assert.Equal("Asignado", activa.GetProperty("estado").GetString());
            Assert.NotEqual(JsonValueKind.Null, activa.GetProperty("recibidaEn").ValueKind);
            Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(appCerca, "/api/v1/conductor/turno/cerrar", new { motivo = "Intento" })).StatusCode);

            // Historial, Outbox y ausencia de ofertas: nada simula una aceptación del conductor.
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioMovilDbContext>();
            Assert.False(await db.Intentos.AnyAsync(i => i.SolicitudId == solicitud));
            var eventos = await db.Set<SolicitudEvento>().Where(e => e.SolicitudId == solicitud).Select(e => e.Tipo).ToListAsync();
            Assert.Contains("AsignadaDirecta", eventos);
            Assert.DoesNotContain(eventos, t => t.StartsWith("Oferta", StringComparison.Ordinal));
            Assert.Equal(1, await db.Outbox.CountAsync(o => o.EmpresaId == f.Empresa && o.Tipo == "Despacho.asignar"));
            Assert.True(await db.AuditoriaAcceso.AnyAsync(a => a.EmpresaId == f.Empresa && a.Tipo == "OrdenRecibida" && a.ObjetoId == viaje));

            // Aislamiento: otra empresa no ve ni asigna esta solicitud.
            using var ajeno = Client(factory);
            using var s5 = await TestIdentity.LoginAndroidAsync(ajeno, await TestIdentity.SeedAsync(factory, Guid.NewGuid()));
            Assert.Equal(HttpStatusCode.NotFound, (await ajeno.GetAsync($"/api/v1/despacho/solicitudes/{otra}/candidatos")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(ajeno, $"/api/v1/despacho/solicitudes/{otra}/asignar", new { conductorId = lejos })).StatusCode);
            Assert.Empty((await GetAsync(ajeno, "/api/v1/despacho/carreras")).EnumerateArray());
        }
        finally { foreach (var s in sesiones) s.Dispose(); }
    }

    [NeonFact]
    public async Task Asignaciones_Concurrentes_UnSoloGanadorPorConductorYPorSolicitud()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(x => x.UseEnvironment("Testing"));
        var (f, sesiones) = await FlotaAsync(factory);
        try
        {
            var (a, _, _) = await ConductorEnTurnoAsync(f, "Conductor A", conAcceso: false);
            var (b, _, _) = await ConductorEnTurnoAsync(f, "Conductor B", conAcceso: false);
            var (c, _, _) = await ConductorEnTurnoAsync(f, "Conductor C", conAcceso: false);
            var s1 = await SolicitudAsync(f); var s2 = await SolicitudAsync(f); var s3 = await SolicitudAsync(f);
            using var operador2 = Client(factory);
            using var t = await TestIdentity.LoginAndroidAsync(operador2, await TestIdentity.SeedAsync(factory, f.Empresa));

            // Dos operadoras asignan el MISMO conductor a solicitudes distintas.
            var mismoConductor = await Task.WhenAll(
                PostAsync(f.Operador, $"/api/v1/despacho/solicitudes/{s1}/asignar", new { conductorId = a }),
                PostAsync(operador2, $"/api/v1/despacho/solicitudes/{s2}/asignar", new { conductorId = a }));
            Assert.Single(mismoConductor, r => r.StatusCode == HttpStatusCode.Created);
            Assert.Single(mismoConductor, r => r.StatusCode == HttpStatusCode.Conflict);

            // Dos operadoras asignan conductores distintos a la MISMA solicitud.
            var mismaSolicitud = await Task.WhenAll(
                PostAsync(f.Operador, $"/api/v1/despacho/solicitudes/{s3}/asignar", new { conductorId = b }),
                PostAsync(operador2, $"/api/v1/despacho/solicitudes/{s3}/asignar", new { conductorId = c }));
            Assert.Single(mismaSolicitud, r => r.StatusCode == HttpStatusCode.Created);
            Assert.Single(mismaSolicitud, r => r.StatusCode == HttpStatusCode.Conflict);

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioMovilDbContext>();
            Assert.Equal(1, await db.Viajes.CountAsync(v => v.ConductorId == a && v.Estado == EstadoViaje.Asignado));
            Assert.Equal(1, await db.Viajes.CountAsync(v => v.SolicitudId == s3));
            foreach (var r in mismoConductor.Concat(mismaSolicitud)) r.Dispose();
        }
        finally { foreach (var s in sesiones) s.Dispose(); }
    }

    [NeonFact]
    public async Task Asignar_Y_CerrarTurno_Concurrentes_NuncaDejanViajeSinTurno()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(x => x.UseEnvironment("Testing"));
        var (f, sesiones) = await FlotaAsync(factory);
        try
        {
            var (conductor, _, _) = await ConductorEnTurnoAsync(f, "Conductor concurrente", conAcceso: false);
            var solicitud = await SolicitudAsync(f);
            var turno = (await GetAsync(f.Operador, $"/api/v1/turnos?abiertos=true&conductorId={conductor}")).GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid();
            var resultados = await Task.WhenAll(
                PostAsync(f.Operador, $"/api/v1/despacho/solicitudes/{solicitud}/asignar", new { conductorId = conductor }),
                PostAsync(f.Admin, $"/api/v1/turnos/{turno}/cerrar", new { motivo = "Fin de jornada" }));
            var asignada = resultados[0].StatusCode == HttpStatusCode.Created;
            var cerrado = resultados[1].StatusCode == HttpStatusCode.OK;
            Assert.True(asignada ^ cerrado, $"asignar={resultados[0].StatusCode} cerrar={resultados[1].StatusCode}");
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioMovilDbContext>();
            var viajeActivo = await db.Viajes.AnyAsync(v => v.ConductorId == conductor && v.Estado == EstadoViaje.Asignado);
            var turnoAbierto = await db.Turnos.AnyAsync(x => x.Id == turno && x.Fin == null);
            Assert.Equal(viajeActivo, turnoAbierto);
            foreach (var r in resultados) r.Dispose();
        }
        finally { foreach (var s in sesiones) s.Dispose(); }
    }

    [NeonFact]
    public async Task Trigger_RechazaViajeActivoSinTurnoAbierto()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(x => x.UseEnvironment("Testing"));
        var (f, sesiones) = await FlotaAsync(factory);
        try
        {
            var conductor = (await BodyAsync(await PostAsync(f.Admin, "/api/v1/conductores", new { nombre = "Sin turno" }))).GetProperty("id").GetGuid();
            var solicitud = await SolicitudAsync(f);
            Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(f.Operador, $"/api/v1/despacho/solicitudes/{solicitud}/asignar", new { conductorId = conductor })).StatusCode);
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioMovilDbContext>();
            var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO viajes ("Id","EmpresaId","SolicitudId","ConductorId","AsignadoEn","Estado")
                VALUES ({Guid.NewGuid()},{f.Empresa},{solicitud},{conductor},{DateTimeOffset.UtcNow},{"Asignado"})
                """));
            Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        }
        finally { foreach (var s in sesiones) s.Dispose(); }
    }

    /// <summary>OSRM falso SOLO para pruebas: responde una ruta sintética fija; nunca se usa como servidor real.</summary>
    private sealed class OsrmFalso : HttpMessageHandler
    {
        public List<Uri> Llamadas { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Llamadas.Add(request.RequestUri!);
            const string json = """{"code":"Ok","routes":[{"distance":1234.4,"duration":301.6,"geometry":{"type":"LineString","coordinates":[[-68.13,-16.5005],[-68.1302,-16.5002],[-68.13,-16.5]]}}]}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") });
        }
    }

    private static WebApplicationFactory<Program> FactoryConRutas(OsrmFalso osrm) => new WebApplicationFactory<Program>().WithWebHostBuilder(x =>
    {
        x.UseEnvironment("Testing");
        x.ConfigureTestServices(s =>
        {
            s.AddSingleton(new RADIOMOVIL.Rutas.RutasOptions { OsrmUrl = "http://osrm.prueba.invalid/" });
            s.AddHttpClient<RADIOMOVIL.Rutas.ServicioRutas>().ConfigurePrimaryHttpMessageHandler(() => osrm);
        });
    });

    private static Task<HttpResponseMessage> PasoAsync(HttpClient app, Guid viaje, string paso, string? key = null, object? body = null) =>
        PostAsync(app, $"/api/v1/conductor/carreras/{viaje}/{paso}", body ?? new { }, key);

    [NeonFact]
    public async Task Conductor_AvanzaCuatroPasos_ConRutaHaciaRecogidaYLuegoDestino()
    {
        var osrm = new OsrmFalso();
        using var factory = FactoryConRutas(osrm);
        var (f, sesiones) = await FlotaAsync(factory);
        try
        {
            var (conductor, app, s3) = await ConductorEnTurnoAsync(f, "Conductor pasos", conAcceso: true);
            var (_, otraApp, s4) = await ConductorEnTurnoAsync(f, "Otro conductor", conAcceso: true);
            using var _s3 = s3; using var _s4 = s4; using var _a = app!; using var _b = otraApp!;
            Assert.Equal(HttpStatusCode.OK, (await app!.PostAsJsonAsync("/api/v1/conductor/ubicaciones", new { latitud = -16.5005, longitud = -68.1300, precisionMetros = 7, capturadaEn = DateTimeOffset.UtcNow })).StatusCode);
            var solicitud = await SolicitudAsync(f, -16.5000, -68.1300, -16.4900, -68.1500);
            var viaje = (await BodyAsync(await PostAsync(f.Operador, $"/api/v1/despacho/solicitudes/{solicitud}/asignar", new { conductorId = conductor }))).GetProperty("viajeId").GetGuid();

            // Sin clave, sin dueño o fuera de orden: nada cambia.
            using (var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/conductor/carreras/{viaje}/en-camino"))
                Assert.Equal(HttpStatusCode.BadRequest, (await app!.SendAsync(request)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await PasoAsync(otraApp!, viaje, "en-camino")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await PasoAsync(f.Operador, viaje, "en-camino")).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await PasoAsync(app, viaje, "iniciar")).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await PasoAsync(app, viaje, "llegada")).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await PasoAsync(app, viaje, "finalizar")).StatusCode);

            var key = Guid.NewGuid().ToString("N");
            using var enCamino = await PasoAsync(app, viaje, "en-camino", key);
            Assert.Equal(HttpStatusCode.OK, enCamino.StatusCode);
            var paso1 = await BodyAsync(enCamino);
            Assert.Equal("EnCamino", paso1.GetProperty("estado").GetString());
            Assert.False(paso1.GetProperty("repetida").GetBoolean());
            var replay = await BodyAsync(await PasoAsync(app, viaje, "en-camino", key));
            Assert.True(replay.GetProperty("repetida").GetBoolean());
            Assert.Equal(paso1.GetProperty("en").GetDateTimeOffset(), replay.GetProperty("en").GetDateTimeOffset());
            Assert.Equal(HttpStatusCode.Conflict, (await PasoAsync(app, viaje, "en-camino")).StatusCode);
            Assert.Equal("EnCamino", Assert.Single((await GetAsync(app, "/api/v1/conductor/carreras")).EnumerateArray()).GetProperty("estado").GetString());

            // Ruta por el servidor propio: hacia la recogida mientras no sube el pasajero.
            Assert.Equal(HttpStatusCode.BadRequest, (await app.GetAsync($"/api/v1/conductor/carreras/{viaje}/ruta?desdeLatitud=95&desdeLongitud=-68.13")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await otraApp!.GetAsync($"/api/v1/conductor/carreras/{viaje}/ruta?desdeLatitud=-16.5&desdeLongitud=-68.13")).StatusCode);
            var ruta = await GetAsync(app, $"/api/v1/conductor/carreras/{viaje}/ruta?desdeLatitud=-16.5005&desdeLongitud=-68.13");
            Assert.Equal("Recogida", ruta.GetProperty("haciaPunto").GetString());
            Assert.Equal(1234, ruta.GetProperty("distanciaMetros").GetDouble());
            Assert.Equal(302, ruta.GetProperty("duracionSegundos").GetDouble());
            Assert.Equal("LineString", ruta.GetProperty("geometria").GetProperty("type").GetString());
            Assert.Equal(3, ruta.GetProperty("geometria").GetProperty("coordinates").GetArrayLength());
            Assert.EndsWith("/route/v1/driving/-68.13,-16.5005;-68.13,-16.5", osrm.Llamadas[^1].AbsolutePath);

            Assert.Equal("EnPuerta", (await BodyAsync(await PasoAsync(app, viaje, "llegada"))).GetProperty("estado").GetString());
            var activa = Assert.Single((await GetAsync(f.Operador, "/api/v1/despacho/carreras")).EnumerateArray());
            Assert.Equal("EnPuerta", activa.GetProperty("estado").GetString());
            Assert.NotEqual(JsonValueKind.Null, activa.GetProperty("ultimoPasoEn").ValueKind);
            Assert.Equal("EnCurso", (await BodyAsync(await PasoAsync(app, viaje, "iniciar"))).GetProperty("estado").GetString());
            Assert.Equal((int)EstadoSolicitud.EnViaje, (await GetAsync(f.Operador, $"/api/v1/solicitudes/{solicitud}")).GetProperty("estado").GetInt32());
            Assert.Equal("Destino", (await GetAsync(app, $"/api/v1/conductor/carreras/{viaje}/ruta?desdeLatitud=-16.5&desdeLongitud=-68.13")).GetProperty("haciaPunto").GetString());
            Assert.EndsWith(";-68.15,-16.49", osrm.Llamadas[^1].AbsolutePath);
            // Mientras hay pasajero a bordo, el turno no se puede cerrar.
            Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(app, "/api/v1/conductor/turno/cerrar", new { motivo = "Intento" })).StatusCode);

            Assert.Equal(HttpStatusCode.BadRequest, (await PasoAsync(app, viaje, "finalizar", body: new { observaciones = new string('x', 501) })).StatusCode);
            var fin = await BodyAsync(await PasoAsync(app, viaje, "finalizar", body: new { observaciones = "Pasajero dejó paraguas" }));
            Assert.Equal("Finalizado", fin.GetProperty("estado").GetString());

            // Cerrada: desaparece de la app, el conductor vuelve a Disponible y la solicitud queda Finalizada.
            Assert.Empty((await GetAsync(app, "/api/v1/conductor/carreras")).EnumerateArray());
            var yo = await GetAsync(app, "/api/v1/conductor/yo");
            Assert.Equal(JsonValueKind.Null, yo.GetProperty("carreraActivaId").ValueKind);
            Assert.Equal("Disponible", yo.GetProperty("estado").GetString());
            Assert.Equal((int)EstadoSolicitud.Finalizada, (await GetAsync(f.Operador, $"/api/v1/solicitudes/{solicitud}")).GetProperty("estado").GetInt32());
            Assert.Empty((await GetAsync(f.Operador, "/api/v1/despacho/carreras")).EnumerateArray());
            Assert.Equal(HttpStatusCode.Conflict, (await PasoAsync(app, viaje, "finalizar")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await app.GetAsync($"/api/v1/conductor/carreras/{viaje}/ruta?desdeLatitud=-16.5&desdeLongitud=-68.13")).StatusCode);

            // Cada paso guarda la hora del servidor y la última posición REAL aceptada del turno.
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioMovilDbContext>();
            var eventos = await db.ViajeEventos.Where(e => e.ViajeId == viaje).OrderBy(e => e.En).ToListAsync();
            Assert.Equal(["EnCamino", "EnPuerta", "EnCurso", "Finalizado"], eventos.Select(e => e.Tipo));
            Assert.All(eventos, e => { Assert.NotNull(e.Posicion); Assert.Equal(-16.5005, e.Posicion!.Y, 6); Assert.Equal(7, e.PrecisionMetros); });
            Assert.Equal("Pasajero dejó paraguas", eventos[^1].Observaciones);
            var tipos = await db.Set<SolicitudEvento>().Where(e => e.SolicitudId == solicitud).Select(e => e.Tipo).ToListAsync();
            Assert.Contains("ViajeIniciado", tipos); Assert.Contains("ViajeFinalizado", tipos);

            // Libre otra vez: puede recibir una nueva carrera.
            var otra = await SolicitudAsync(f, -16.5, -68.13);
            Assert.Equal(HttpStatusCode.Created, (await PostAsync(f.Operador, $"/api/v1/despacho/solicitudes/{otra}/asignar", new { conductorId = conductor })).StatusCode);
        }
        finally { foreach (var s in sesiones) s.Dispose(); }
    }

    [NeonFact]
    public async Task Central_CancelaEnCamino_ConductorYaNoPuedeAvanzar_YRutaSinServidorEs503()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(x => x.UseEnvironment("Testing"));
        var (f, sesiones) = await FlotaAsync(factory);
        try
        {
            var (conductor, app, s3) = await ConductorEnTurnoAsync(f, "Conductor cancelado", conAcceso: true);
            using var _s3 = s3; using var _a = app!;
            var solicitud = await SolicitudAsync(f, -16.5, -68.13);
            var viaje = (await BodyAsync(await PostAsync(f.Operador, $"/api/v1/despacho/solicitudes/{solicitud}/asignar", new { conductorId = conductor }))).GetProperty("viajeId").GetGuid();
            Assert.Equal(HttpStatusCode.OK, (await PasoAsync(app!, viaje, "en-camino")).StatusCode);
            // Sin servidor de rutas configurado: 503 y la app muestra solo los puntos.
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await app!.GetAsync($"/api/v1/conductor/carreras/{viaje}/ruta?desdeLatitud=-16.5005&desdeLongitud=-68.13")).StatusCode);

            Assert.Equal(HttpStatusCode.OK, (await PostAsync(f.Operador, $"/api/v1/despacho/solicitudes/{solicitud}/cancelar", new { motivo = "Cliente desiste" })).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await PasoAsync(app, viaje, "llegada")).StatusCode);
            Assert.Empty((await GetAsync(app, "/api/v1/conductor/carreras")).EnumerateArray());
            Assert.Equal(JsonValueKind.Null, (await GetAsync(app, "/api/v1/conductor/yo")).GetProperty("carreraActivaId").ValueKind);
        }
        finally { foreach (var s in sesiones) s.Dispose(); }
    }

    [NeonFact]
    public async Task PasoYCancelacion_Concurrentes_NuncaDejanSolicitudYViajeIncoherentes()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(x => x.UseEnvironment("Testing"));
        var (f, sesiones) = await FlotaAsync(factory);
        try
        {
            var (conductor, app, s3) = await ConductorEnTurnoAsync(f, "Conductor carrera", conAcceso: true);
            using var _s3 = s3; using var _a = app!;
            for (var i = 0; i < 3; i++)
            {
                var solicitud = await SolicitudAsync(f, -16.5, -68.13);
                var viaje = (await BodyAsync(await PostAsync(f.Operador, $"/api/v1/despacho/solicitudes/{solicitud}/asignar", new { conductorId = conductor }))).GetProperty("viajeId").GetGuid();
                Assert.Equal(HttpStatusCode.OK, (await PasoAsync(app!, viaje, "en-camino")).StatusCode);
                var iniciar = PasoAsync(app!, viaje, "iniciar");
                var cancelar = PostAsync(f.Operador, $"/api/v1/despacho/solicitudes/{solicitud}/cancelar", new { motivo = "Carrera concurrente" });
                await Task.WhenAll(iniciar, cancelar);
                var okIniciar = iniciar.Result.StatusCode == HttpStatusCode.OK; var okCancelar = cancelar.Result.StatusCode == HttpStatusCode.OK;
                Assert.True(okIniciar ^ okCancelar, $"iniciar {iniciar.Result.StatusCode}, cancelar {cancelar.Result.StatusCode}");
                await using var scope = factory.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<RadioMovilDbContext>();
                var estadoViaje = await db.Viajes.Where(v => v.Id == viaje).Select(v => v.Estado).SingleAsync();
                var estadoSolicitud = await db.Solicitudes.Where(s => s.Id == solicitud).Select(s => s.Estado).SingleAsync();
                if (okIniciar) { Assert.Equal(EstadoViaje.EnCurso, estadoViaje); Assert.Equal(EstadoSolicitud.EnViaje, estadoSolicitud);
                    Assert.Equal(HttpStatusCode.OK, (await PasoAsync(app!, viaje, "finalizar")).StatusCode); }
                else { Assert.Equal(EstadoViaje.Incidencia, estadoViaje); Assert.Equal(EstadoSolicitud.Cancelada, estadoSolicitud); }
            }
        }
        finally { foreach (var s in sesiones) s.Dispose(); }
    }
}
