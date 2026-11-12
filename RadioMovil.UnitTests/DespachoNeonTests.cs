using Microsoft.EntityFrameworkCore;
using Npgsql;
using RadioMovil.Domain;
using RadioMovil.Infrastructure;
using Xunit;

namespace RadioMovil.UnitTests;

public sealed class DespachoNeonTests
{
    private static string Connection => NeonTestingGuard.RequireTesting(Environment.GetEnvironmentVariable("NEON_TESTING_DIRECT_CONNECTION_STRING"), requireDirect: true);
    private static RadioMovilDbContext Open() => new(new DbContextOptionsBuilder<RadioMovilDbContext>()
        .UseNpgsql(Connection, x => x.UseNetTopologySuite()).Options);
    private sealed record Seed(Guid Empresa, Guid Actor, Guid Conductor, Guid OtroConductor, Guid Solicitud, Guid OtraSolicitud);

    private static async Task<Seed> Setup()
    {
        var empresa = Guid.NewGuid(); var actor = Guid.NewGuid();
        await using var db = Open();
        var first = new Conductor(empresa, "Operador de prueba uno");
        var second = new Conductor(empresa, "Operador de prueba dos");
        var vehicle1 = new Vehiculo(empresa, "RM" + Guid.NewGuid().ToString("N")[..10]);
        var vehicle2 = new Vehiculo(empresa, "RM" + Guid.NewGuid().ToString("N")[..10]);
        db.Conductores.AddRange(first, second);
        db.Vehiculos.AddRange(vehicle1, vehicle2);
        db.Turnos.AddRange(new Turno(empresa, first.Id, vehicle1.Id, DateTimeOffset.UtcNow.AddMinutes(-1)),
            new Turno(empresa, second.Id, vehicle2.Id, DateTimeOffset.UtcNow.AddMinutes(-1)));
        await db.SaveChangesAsync();
        var central = Guid.NewGuid();
        await TestCentrales.CreateAsync(db, empresa, central);
        var (s1, _) = await new RegistroSolicitudes(db).RegistrarAsync(empresa, central, actor, "Origen 1", Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow);
        var (s2, _) = await new RegistroSolicitudes(db).RegistrarAsync(empresa, central, actor, "Origen 2", Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow);
        return new(empresa, actor, first.Id, second.Id, s1, s2);
    }

    private static async Task<Resultado> Call(Func<DespachoManualPostgres, Task<RadioMovil.Application.ResultadoDespacho>> action)
    {
        await using var db = Open();
        try { return new(await action(new DespachoManualPostgres(db)), null); }
        catch (Exception e) when (e is DespachoConflictoException or SolicitudAjenaONoEncontradaException or ErrorDominio or DbUpdateException or PostgresException)
        { return new(null, e); }
    }
    private sealed record Resultado(RadioMovil.Application.ResultadoDespacho? Value, Exception? Error);

    [NeonFact]
    public async Task Flujo_OfertaRechazoAceptacionYCancelacionConductor_ConHistorialEIdempotencia()
    {
        var s = await Setup();
        await using var db = Open();
        var dispatch = new DespachoManualPostgres(db);
        var candidates = await dispatch.CandidatosAsync(s.Empresa);
        Assert.Equal(2, candidates.Count);
        Assert.All(candidates, c => Assert.False(c.UbicacionVerificada));
        var offerKey = Guid.NewGuid().ToString("N");
        var first = await dispatch.OfrecerAsync(s.Empresa, s.Solicitud, s.Conductor, s.Actor, offerKey);
        Assert.Equal(EstadoSolicitud.Ofertada, first.Estado);
        Assert.Equal(first.IntentoId, (await Call(x => x.OfrecerAsync(s.Empresa, s.Solicitud, s.Conductor, s.Actor, offerKey))).Value!.IntentoId);
        Assert.True((await Call(x => x.OfrecerAsync(s.Empresa, s.Solicitud, s.OtroConductor, s.Actor, offerKey))).Error is DespachoConflictoException);
        var rejectKey = Guid.NewGuid().ToString("N");
        var rejected = await Call(x => x.RechazarAsync(s.Empresa, s.Solicitud, first.IntentoId!.Value, s.Actor, "Rechaza", rejectKey));
        Assert.Equal(EstadoSolicitud.Pendiente, rejected.Value!.Estado);
        Assert.True((await Call(x => x.RechazarAsync(s.Empresa, s.Solicitud, first.IntentoId!.Value, s.Actor, "Rechaza", rejectKey))).Value!.Repetida);
        Assert.True((await Call(x => x.RechazarAsync(s.Empresa, s.Solicitud, first.IntentoId!.Value, s.Actor, "Otro motivo", rejectKey))).Error is DespachoConflictoException);
        var second = await Call(x => x.OfrecerAsync(s.Empresa, s.Solicitud, s.OtroConductor, s.Actor, Guid.NewGuid().ToString("N")));
        Assert.Equal(EstadoSolicitud.Ofertada, second.Value!.Estado);
        var acceptKey = Guid.NewGuid().ToString("N");
        var accepted = await Call(x => x.AceptarAsync(s.Empresa, s.Solicitud, second.Value.IntentoId!.Value, s.Actor, acceptKey));
        Assert.Equal(EstadoSolicitud.Asignada, accepted.Value!.Estado);
        var replay = await Call(x => x.AceptarAsync(s.Empresa, s.Solicitud, second.Value.IntentoId!.Value, s.Actor, acceptKey));
        Assert.True(replay.Value!.Repetida);
        Assert.Equal(accepted.Value.ViajeId, replay.Value.ViajeId);
        var conductorKey = Guid.NewGuid().ToString("N");
        var canceled = await Call(x => x.CancelarConductorAsync(s.Empresa, s.Solicitud, accepted.Value.ViajeId!.Value, s.Actor, "Antes de recoger", conductorKey));
        Assert.Equal(EstadoSolicitud.Pendiente, canceled.Value!.Estado);
        Assert.True((await Call(x => x.CancelarConductorAsync(s.Empresa, s.Solicitud, accepted.Value.ViajeId!.Value, s.Actor, "Antes de recoger", conductorKey))).Value!.Repetida);
        await using var check = Open();
        var solicitud = await check.Solicitudes.Include(x => x.Intentos).Include(x => x.Eventos).SingleAsync(x => x.Id == s.Solicitud);
        Assert.Null(solicitud.ViajeId);
        Assert.Equal(6, solicitud.Eventos.Count); // Creada, oferta, rechazo, oferta, aceptación, cancelación
        Assert.Equal(EstadoViaje.Incidencia, (await check.Viajes.SingleAsync(x => x.Id == accepted.Value.ViajeId)).Estado);
        Assert.Equal(EstadoIntento.Aceptado, (await check.Intentos.SingleAsync(x => x.Id == second.Value.IntentoId)).Estado);
        Assert.Equal(5, await check.Outbox.CountAsync(x => x.EmpresaId == s.Empresa));
        Assert.Equal(2, (await dispatch.CandidatosAsync(s.Empresa)).Count);
    }

    [NeonFact]
    public async Task Competencias_DosSolicitudesMismoConductor_YDosConductoresMismaSolicitud()
    {
        var s = await Setup();
        var a = await Task.WhenAll(
            Call(x => x.OfrecerAsync(s.Empresa, s.Solicitud, s.Conductor, s.Actor, Guid.NewGuid().ToString("N"))),
            Call(x => x.OfrecerAsync(s.Empresa, s.OtraSolicitud, s.Conductor, s.Actor, Guid.NewGuid().ToString("N"))));
        Assert.Single(a, x => x.Value is not null);
        Assert.Single(a, x => x.Error is not null);
        var winner = Assert.Single(a, x => x.Value is not null).Value!;
        await using (var db = Open())
        {
            var active = await db.Intentos.CountAsync(x => x.EmpresaId == s.Empresa && x.ConductorId == s.Conductor && x.Estado == EstadoIntento.Ofertado);
            Assert.Equal(1, active);
        }
        var pending = winner.SolicitudId == s.Solicitud ? s.OtraSolicitud : s.Solicitud;
        var b = await Task.WhenAll(
            Call(x => x.OfrecerAsync(s.Empresa, pending, s.OtroConductor, s.Actor, Guid.NewGuid().ToString("N"))),
            Call(x => x.OfrecerAsync(s.Empresa, pending, s.OtroConductor, s.Actor, Guid.NewGuid().ToString("N"))));
        Assert.Single(b, x => x.Value is not null);
        Assert.Single(b, x => x.Error is not null);
        await using var check = Open();
        Assert.Equal(1, await check.Intentos.CountAsync(x => x.SolicitudId == pending && x.Estado == EstadoIntento.Ofertado));
    }

    [NeonFact]
    public async Task MismaSolicitudDosConductores_YMismaClaveConcurrente_NoDuplicanOferta()
    {
        var s = await Setup();
        var offers = await Task.WhenAll(
            Call(x => x.OfrecerAsync(s.Empresa, s.Solicitud, s.Conductor, s.Actor, Guid.NewGuid().ToString("N"))),
            Call(x => x.OfrecerAsync(s.Empresa, s.Solicitud, s.OtroConductor, s.Actor, Guid.NewGuid().ToString("N"))));
        Assert.Single(offers, x => x.Value is not null);
        Assert.Single(offers, x => x.Error is not null);
        Guid freeDriver;
        await using (var db = Open())
        {
            var active = await db.Intentos.SingleAsync(x => x.SolicitudId == s.Solicitud && x.Estado == EstadoIntento.Ofertado);
            freeDriver = active.ConductorId == s.Conductor ? s.OtroConductor : s.Conductor;
        }

        var key = Guid.NewGuid().ToString("N");
        var repeats = await Task.WhenAll(
            Call(x => x.OfrecerAsync(s.Empresa, s.OtraSolicitud, freeDriver, s.Actor, key)),
            Call(x => x.OfrecerAsync(s.Empresa, s.OtraSolicitud, freeDriver, s.Actor, key)));
        Assert.All(repeats, x => Assert.Null(x.Error));
        Assert.Single(repeats, x => x.Value?.Repetida == true);
        Assert.Equal(repeats[0].Value?.IntentoId, repeats[1].Value?.IntentoId);
        await using var verify = Open();
        Assert.Equal(1, await verify.Intentos.CountAsync(x => x.SolicitudId == s.OtraSolicitud && x.Estado == EstadoIntento.Ofertado));
        Assert.Equal(2, await verify.Outbox.CountAsync(x => x.EmpresaId == s.Empresa));
    }

    [NeonFact]
    public async Task AceptacionYCancelacionCliente_Concurrentes_DejanEstadosCompatibles()
    {
        var s = await Setup();
        var offer = (await Call(x => x.OfrecerAsync(s.Empresa, s.Solicitud, s.Conductor, s.Actor, Guid.NewGuid().ToString("N")))).Value!;
        var results = await Task.WhenAll(
            Call(x => x.AceptarAsync(s.Empresa, s.Solicitud, offer.IntentoId!.Value, s.Actor, Guid.NewGuid().ToString("N"))),
            Call(x => x.CancelarClienteAsync(s.Empresa, s.Solicitud, s.Actor, "Cancelada por cliente", Guid.NewGuid().ToString("N"))));
        Assert.Single(results, x => x.Value?.Estado == EstadoSolicitud.Cancelada);
        await using var check = Open();
        var solicitud = await check.Solicitudes.SingleAsync(x => x.Id == s.Solicitud);
        Assert.Equal(EstadoSolicitud.Cancelada, solicitud.Estado);
        Assert.Null(solicitud.ViajeId);
        Assert.Equal(0, await check.Viajes.CountAsync(x => x.ConductorId == s.Conductor && x.Estado == EstadoViaje.Asignado));
        Assert.Equal(0, await check.Intentos.CountAsync(x => x.ConductorId == s.Conductor && x.Estado == EstadoIntento.Ofertado));
        Assert.True(await check.Eventos.CountAsync(x => x.SolicitudId == s.Solicitud) >= 3);
    }

    [NeonFact]
    public async Task VencimientoYRespuestaTardia_NoCreanViaje()
    {
        var s = await Setup();
        var offer = (await Call(x => x.OfrecerAsync(s.Empresa, s.Solicitud, s.Conductor, s.Actor, Guid.NewGuid().ToString("N")))).Value!;
        await using (var db = Open())
        {
            var vence = (await db.Intentos.SingleAsync(x => x.Id == offer.IntentoId)).VenceEn;
            var delay = vence - DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(100);
            if (delay > TimeSpan.Zero) await Task.Delay(delay);
        }
        var expiryKey = Guid.NewGuid().ToString("N");
        var attempts = await Task.WhenAll(
            Call(x => x.AceptarAsync(s.Empresa, s.Solicitud, offer.IntentoId!.Value, s.Actor, Guid.NewGuid().ToString("N"))),
            Call(x => x.VencerAsync(s.Empresa, s.Solicitud, offer.IntentoId!.Value, s.Actor, expiryKey)));
        Assert.Null(attempts[0].Value);
        Assert.Equal(EstadoSolicitud.Pendiente, attempts[1].Value!.Estado);
        Assert.True((await Call(x => x.VencerAsync(s.Empresa, s.Solicitud, offer.IntentoId!.Value, s.Actor, expiryKey))).Value!.Repetida);
        await using var check = Open();
        Assert.Equal(EstadoIntento.Vencido, (await check.Intentos.SingleAsync(x => x.Id == offer.IntentoId)).Estado);
        Assert.False(await check.Viajes.AnyAsync(x => x.SolicitudId == s.Solicitud));
    }

    [NeonFact]
    public async Task AislamientoRollbackYCancelacionAsignada_NoDejanReserva()
    {
        var s = await Setup();
        var other = await Setup();
        Assert.True((await Call(x => x.OfrecerAsync(other.Empresa, s.Solicitud, s.Conductor, other.Actor, Guid.NewGuid().ToString("N")))).Error is SolicitudAjenaONoEncontradaException);
        await using (var db = Open())
        {
            var actor = s.Actor; var key = Guid.NewGuid().ToString("N");
            db.Eventos.Add(new SolicitudEvento(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, actor, "Prueba", "FK"));
            await Assert.ThrowsAsync<DbUpdateException>(() => new DespachoManualPostgres(db).OfrecerAsync(s.Empresa, s.Solicitud, s.Conductor, s.Actor, key));
            await using var verify = Open();
            Assert.False(await verify.OperacionesDespacho.AnyAsync(x => x.EmpresaId == s.Empresa && x.Clave == key));
            Assert.False(await verify.Intentos.AnyAsync(x => x.SolicitudId == s.Solicitud));
        }
        var offer = (await Call(x => x.OfrecerAsync(s.Empresa, s.Solicitud, s.Conductor, s.Actor, Guid.NewGuid().ToString("N")))).Value!;
        var accept = (await Call(x => x.AceptarAsync(s.Empresa, s.Solicitud, offer.IntentoId!.Value, s.Actor, Guid.NewGuid().ToString("N")))).Value!;
        var keyCancel = Guid.NewGuid().ToString("N");
        var canceled = (await Call(x => x.CancelarClienteAsync(s.Empresa, s.Solicitud, s.Actor, "Sin pasajero", keyCancel))).Value!;
        Assert.Equal(EstadoSolicitud.Cancelada, canceled.Estado);
        Assert.True((await Call(x => x.CancelarClienteAsync(s.Empresa, s.Solicitud, s.Actor, "Sin pasajero", keyCancel))).Value!.Repetida);
        await using var final = Open();
        Assert.Equal(EstadoViaje.Incidencia, (await final.Viajes.SingleAsync(x => x.Id == accept.ViajeId)).Estado);
        Assert.Null((await final.Solicitudes.SingleAsync(x => x.Id == s.Solicitud)).ViajeId);
        Assert.False(await final.Intentos.AnyAsync(x => x.SolicitudId == s.Solicitud && x.Estado == EstadoIntento.Ofertado));
        Assert.Contains(await new DespachoManualPostgres(final).CandidatosAsync(s.Empresa), x => x.ConductorId == s.Conductor);
    }
}
