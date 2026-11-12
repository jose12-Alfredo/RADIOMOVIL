using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RadioMovil.Application;
using RadioMovil.Domain;
using RadioMovil.Infrastructure;
using RadioMovil.Workers;
using Xunit;

namespace RadioMovil.UnitTests;

public sealed class WorkersNeonTests
{
    private static RadioMovilDbContext Open() => new(new DbContextOptionsBuilder<RadioMovilDbContext>()
        .UseNpgsql(NeonTestingGuard.RequireTesting(Environment.GetEnvironmentVariable("NEON_TESTING_DIRECT_CONNECTION_STRING"), requireDirect: true), x => x.UseNetTopologySuite()).Options);
    private sealed record Seed(Guid Empresa, Guid Actor, Guid Solicitud, Guid Conductor);
    private static async Task<Seed> SetupAsync()
    {
        var empresa = Guid.NewGuid(); var actor = Guid.NewGuid();
        await using var db = Open();
        var driver = new Conductor(empresa, "Worker conductor");
        var car = new Vehiculo(empresa, "WK" + Guid.NewGuid().ToString("N")[..10]);
        db.Conductores.Add(driver); db.Vehiculos.Add(car);
        db.Turnos.Add(new Turno(empresa, driver.Id, car.Id, DateTimeOffset.UtcNow.AddMinutes(-5)));
        await db.SaveChangesAsync();
        var central = await TestCentrales.CreateAsync(db, empresa);
        var (id, _) = await new RegistroSolicitudes(db).RegistrarAsync(empresa, central, actor, "Testing", Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow);
        return new(empresa, actor, id, driver.Id);
    }
    private static WorkerProcessor Processor(RadioMovilDbContext db, Guid actor) =>
        new(db, new DespachoManualPostgres(db), new WorkerOptions { ActorId = actor, BatchSize = 16, Interval = TimeSpan.FromSeconds(1) },
            LoggerFactory.Create(_ => { }).CreateLogger<WorkerProcessor>());
    private static async Task<Guid> ExpiredOfferAsync(Seed s)
    {
        await using var db = Open();
        var offer = await new DespachoManualPostgres(db).OfrecerAsync(s.Empresa, s.Solicitud, s.Conductor, s.Actor, Guid.NewGuid().ToString("N"));
        // Acortar solo el tiempo de prueba, manteniendo la restricción VenceEn > CreadoEn.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE intentos_despacho SET "CreadoEn" = clock_timestamp() - interval '3 seconds',
              "VenceEn" = clock_timestamp() - interval '1 second' WHERE "Id" = {offer.IntentoId!.Value}
            """);
        return offer.IntentoId.Value;
    }
    private static async Task AssertExpiredAsync(Seed s, Guid offer)
    {
        await using var verify = Open();
        Assert.Equal(EstadoSolicitud.Pendiente, (await verify.Solicitudes.SingleAsync(x => x.Id == s.Solicitud)).Estado);
        Assert.Equal(EstadoIntento.Vencido, (await verify.Intentos.SingleAsync(x => x.Id == offer)).Estado);
        Assert.Equal(1, await verify.Eventos.CountAsync(x => x.SolicitudId == s.Solicitud && x.Tipo == "OfertaVencida"));
        Assert.Equal(2, await verify.Outbox.CountAsync(x => x.EmpresaId == s.Empresa));
        Assert.Equal(2, await verify.Outbox.CountAsync(x => x.EmpresaId == s.Empresa && x.PublicadoEn == null));
        Assert.False(await verify.Viajes.AnyAsync(x => x.SolicitudId == s.Solicitud));
        Assert.Contains(await new DespachoManualPostgres(verify).CandidatosAsync(s.Empresa), c => c.ConductorId == s.Conductor);
    }

    [NeonFact]
    public async Task ExpiracionAutonomaYReinicio_RecuperaOfertaAbandonadaSinPerderSolicitud()
    {
        var s = await SetupAsync();
        var offer = await ExpiredOfferAsync(s);
        // Servicio comienza después del reinicio: no se necesita recordar nada en memoria.
        var services = new ServiceCollection().AddLogging()
            .AddPersistencia(NeonTestingGuard.RequireTesting(Environment.GetEnvironmentVariable("NEON_TESTING_DIRECT_CONNECTION_STRING"), requireDirect: true))
            .AddRadioMovilWorkers(new WorkerOptions { ActorId = s.Actor, BatchSize = 4, Interval = TimeSpan.FromSeconds(1) })
            .BuildServiceProvider();
        await using (services)
        {
            var worker = services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<OfferExpirationWorker>().Single();
            await worker.StartAsync(CancellationToken.None);
            try
            {
                var done = false;
                for (var i = 0; i < 30; i++)
                {
                    await Task.Delay(1000);
                    await using var check = Open();
                    done = await check.Intentos.AnyAsync(x => x.Id == offer && x.Estado == EstadoIntento.Vencido);
                    if (done) break;
                }
                Assert.True(done, "El BackgroundService no venció la oferta.");
            }
            finally { await worker.StopAsync(CancellationToken.None); }
        }
        await AssertExpiredAsync(s, offer);
    }

    [NeonFact]
    public async Task DosInstanciasYSolicitudAntigua_NoDuplicanNiAbandonanPendientes()
    {
        var s = await SetupAsync(); var offer = await ExpiredOfferAsync(s);
        var old = await SetupAsync();
        await using (var age = Open())
            await age.Database.ExecuteSqlInterpolatedAsync($"UPDATE solicitudes SET \"CreadaEn\" = clock_timestamp() - interval '7 days' WHERE \"Id\" = {old.Solicitud}");
        await using var first = Open(); await using var second = Open();
        await Task.WhenAll(Processor(first, s.Actor).ExpireBatchAsync(), Processor(second, s.Actor).ExpireBatchAsync());
        await AssertExpiredAsync(s, offer);
        await using var check = Open();
        await Processor(check, s.Actor).ReconcileBatchAsync();
        Assert.False(await check.IncidenciasWorker.AnyAsync(x => x.EmpresaId == s.Empresa && x.AtendidaEn == null));
        Assert.False(await check.IncidenciasWorker.AnyAsync(x => x.EmpresaId == old.Empresa && x.AtendidaEn == null));
        Assert.Equal(EstadoSolicitud.Pendiente, (await check.Solicitudes.SingleAsync(x => x.Id == old.Solicitud)).Estado);
        Assert.Single(await check.Eventos.Where(x => x.SolicitudId == old.Solicitud).ToListAsync());
    }

    [NeonFact]
    public async Task AceptacionSimultaneaConVencimiento_NoCreaViajeIncompatible()
    {
        var s = await SetupAsync(); var offer = await ExpiredOfferAsync(s);
        await using var a = Open(); await using var b = Open();
        var acceptance = Task.Run(async () =>
        {
            try { return await new DespachoManualPostgres(a).AceptarAsync(s.Empresa, s.Solicitud, offer, s.Actor, Guid.NewGuid().ToString("N")); }
            catch (ErrorDominio) { return null; }
        });
        await Task.WhenAll(acceptance, Processor(b, s.Actor).ExpireBatchAsync());
        Assert.Null(acceptance.Result);
        await AssertExpiredAsync(s, offer);
    }

    [NeonFact]
    public async Task ReinicioDuranteBloqueo_NoPierdeOfertaNiDuplicaTransicion()
    {
        var s = await SetupAsync(); var offer = await ExpiredOfferAsync(s);
        await using var blocker = Open();
        await using var tx = await blocker.Database.BeginTransactionAsync();
        // Fuerza a VencerAsync a esperar el bloqueo de solicitud, sin confirmar
        // ninguna escritura del test. La cancelación representa parada del host.
        await blocker.Database.ExecuteSqlInterpolatedAsync($"UPDATE solicitudes SET \"Estado\" = \"Estado\" WHERE \"Id\" = {s.Solicitud}");
        await using var firstInstance = Open();
        using var stop = new CancellationTokenSource();
        var inFlight = Processor(firstInstance, s.Actor).ExpireBatchAsync(stop.Token);
        await Task.Delay(2500);
        Assert.False(inFlight.IsCompleted, "La operación debía esperar el bloqueo antes del reinicio.");
        stop.Cancel();
        try { await Assert.ThrowsAnyAsync<OperationCanceledException>(() => inFlight); }
        finally { await tx.RollbackAsync(); }
        await using (var verify = Open())
        {
            Assert.Equal(EstadoIntento.Ofertado, (await verify.Intentos.SingleAsync(x => x.Id == offer)).Estado);
            Assert.Equal(0, await verify.Eventos.CountAsync(x => x.SolicitudId == s.Solicitud && x.Tipo == "OfertaVencida"));
        }
        await using (var restarted = Open()) await Processor(restarted, s.Actor).ExpireBatchAsync();
        await AssertExpiredAsync(s, offer);
    }

    [NeonFact]
    public async Task RecoveryWorker_ReintentaIncidenciaTrasReinicio_YReconciliacionAlertaIncoherencia()
    {
        var s = await SetupAsync(); var offer = await ExpiredOfferAsync(s);
        await using (var db = Open())
        {
            db.IncidenciasWorker.Add(new IncidenciaWorker { EmpresaId = s.Empresa, Tipo = "VencimientoFallido", ReferenciaId = offer,
                PrimeraDeteccion = DateTimeOffset.UtcNow.AddMinutes(-1), UltimaDeteccion = DateTimeOffset.UtcNow.AddMinutes(-1),
                ProximoIntento = DateTimeOffset.UtcNow.AddMinutes(-1), Intentos = 1 });
            await db.SaveChangesAsync();
        }
        await using (var restarted = Open()) Assert.Equal(1, await Processor(restarted, s.Actor).RecoverBatchAsync());
        await AssertExpiredAsync(s, offer);
        await using (var verify = Open())
            Assert.NotNull((await verify.IncidenciasWorker.SingleAsync(x => x.EmpresaId == s.Empresa && x.ReferenciaId == offer)).AtendidaEn);

        // Incoherencia intencional de una fila de Testing aislada por GUID: no hay
        // transición segura para inventar una oferta o asignar un conductor.
        await using var corrupt = Open();
        await corrupt.Database.ExecuteSqlInterpolatedAsync($"UPDATE solicitudes SET \"Estado\" = 'Ofertada' WHERE \"Id\" = {s.Solicitud}");
        try
        {
            await using (var reconcile = Open()) await Processor(reconcile, s.Actor).ReconcileBatchAsync();
            await using var final = Open();
            Assert.True(await final.IncidenciasWorker.AnyAsync(x => x.EmpresaId == s.Empresa && x.ReferenciaId == s.Solicitud &&
                x.Tipo == "EstadoIncoherente" && x.AtendidaEn == null));
            Assert.False(await final.Viajes.AnyAsync(x => x.SolicitudId == s.Solicitud));
        }
        finally
        {
            // Restaurar la anomalía artificial sin borrar historia ni la alerta persistente.
            await corrupt.Database.ExecuteSqlInterpolatedAsync($"UPDATE solicitudes SET \"Estado\" = 'Pendiente' WHERE \"Id\" = {s.Solicitud}");
        }
    }

    [NeonFact]
    public async Task ErrorPersistente_AgotarReintentosSinBucleNiReservaNueva()
    {
        var s = await SetupAsync(); var offer = await ExpiredOfferAsync(s);
        await using var db = Open();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE solicitudes SET \"Estado\" = 'Pendiente' WHERE \"Id\" = {s.Solicitud}");
        try
        {
            await using (var scan = Open()) await Processor(scan, s.Actor).ExpireBatchAsync();
            await using (var seed = Open())
            {
                var issue = await seed.IncidenciasWorker.SingleAsync(x => x.EmpresaId == s.Empresa && x.ReferenciaId == offer);
                Assert.Equal("VencimientoFallido", issue.Tipo);
                issue.Intentos = 4;
                issue.ProximoIntento = DateTimeOffset.UtcNow.AddMinutes(-1);
                await seed.SaveChangesAsync();
            }
            await using (var retry = Open()) Assert.Equal(1, await Processor(retry, s.Actor).RecoverBatchAsync());
            await using (var final = Open())
            {
                Assert.Equal(0, await Processor(final, s.Actor).RecoverBatchAsync());
                var issue = await final.IncidenciasWorker.SingleAsync(x => x.EmpresaId == s.Empresa && x.ReferenciaId == offer);
                Assert.Equal(5, issue.Intentos);
                Assert.Null(issue.AtendidaEn); // requiere intervención
                Assert.False(await final.Viajes.AnyAsync(x => x.SolicitudId == s.Solicitud));
            }
        }
        finally
        {
            // El test indujo una anomalía; no dejarla persistida en la solicitud.
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE solicitudes SET \"Estado\" = 'Ofertada' WHERE \"Id\" = {s.Solicitud}");
            await using var cleaned = Open();
            await new DespachoManualPostgres(cleaned).VencerAsync(s.Empresa, s.Solicitud, offer, s.Actor, "test-manual-recovery:" + offer.ToString("N"));
            await cleaned.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE incidencias_worker SET "AtendidaEn" = clock_timestamp()
                WHERE "EmpresaId" = {s.Empresa} AND "Tipo" = 'VencimientoFallido' AND "ReferenciaId" = {offer}
                """);
        }
    }
}
