using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RadioMovil.Application;
using RadioMovil.Domain;
using RadioMovil.Infrastructure;

namespace RadioMovil.Workers;

public sealed class WorkerOptions
{
    public int BatchSize { get; init; } = 16;
    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(5);
    public Guid ActorId { get; init; }
}

public sealed class WorkerProcessor(RadioMovilDbContext db, IDespachoManual despacho,
    WorkerOptions options, ILogger<WorkerProcessor> logger)
{
    private sealed class OfferRow
    {
        public Guid EmpresaId { get; set; }
        public Guid SolicitudId { get; set; }
        public Guid IntentoId { get; set; }
    }
    private sealed class AlertRow
    {
        public Guid EmpresaId { get; set; }
        public Guid ReferenciaId { get; set; }
        public string Tipo { get; set; } = "";
    }

    // Sin leases en vencimientos: la selección no reserva nada. El bloqueo FOR UPDATE
    // del despacho y su clave estable serializan la transición, incluso entre procesos.
    public async Task<int> ExpireBatchAsync(CancellationToken ct = default)
    {
        var rows = await db.Database.SqlQuery<OfferRow>($"""
            SELECT i."EmpresaId", i."SolicitudId", i."Id" AS "IntentoId"
            FROM intentos_despacho i
            WHERE i."Estado" = 'Ofertado' AND i."VenceEn" <= clock_timestamp()
              AND NOT EXISTS (SELECT 1 FROM incidencias_worker a
                  WHERE a."EmpresaId" = i."EmpresaId" AND a."Tipo" = 'VencimientoFallido'
                    AND a."ReferenciaId" = i."Id" AND a."AtendidaEn" IS NULL)
            ORDER BY i."VenceEn", i."Id" LIMIT {options.BatchSize}
            """).ToListAsync(ct);
        foreach (var row in rows) await ExpireAsync(row, ct);
        return rows.Count;
    }

    // La interrupción antes del COMMIT deja la oferta intacta: el siguiente escaneo
    // la encuentra. Tras COMMIT, la clave y el estado evitan un segundo vencimiento.
    private async Task ExpireAsync(OfferRow row, CancellationToken ct)
    {
        try
        {
            await despacho.VencerAsync(row.EmpresaId, row.SolicitudId, row.IntentoId,
                options.ActorId, "worker-expire:" + row.IntentoId.ToString("N"), ct);
            await ResolveAlertAsync(row.EmpresaId, "VencimientoFallido", row.IntentoId, ct);
        }
        catch (Exception ex) when (ex is ErrorDominio or DespachoConflictoException)
        {
            // Otro operador/worker pudo resolverla durante la selección. No declarar
            // éxito sin consultar el estado nuevamente en PostgreSQL.
            db.ChangeTracker.Clear();
            if (await db.Intentos.AsNoTracking().AnyAsync(i => i.Id == row.IntentoId && i.EmpresaId == row.EmpresaId && i.Estado != EstadoIntento.Ofertado, ct))
            {
                await ResolveAlertAsync(row.EmpresaId, "VencimientoFallido", row.IntentoId, ct);
                return;
            }
            await RecordAlertAsync(row.EmpresaId, "VencimientoFallido", row.IntentoId, true, ct);
            logger.LogError(ex, "Oferta {IntentoId} requiere intervención tras fallo de vencimiento", row.IntentoId);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            // Un fallo de red puede significar COMMIT desconocido: reconsulta primero.
            logger.LogError(ex, "Vencimiento incierto de oferta {IntentoId}", row.IntentoId);
            db.ChangeTracker.Clear();
            try
            {
                if (await db.Intentos.AsNoTracking().AnyAsync(i => i.Id == row.IntentoId && i.EmpresaId == row.EmpresaId && i.Estado == EstadoIntento.Ofertado, ct))
                    await RecordAlertAsync(row.EmpresaId, "VencimientoFallido", row.IntentoId, true, ct);
            }
            catch (Exception checkError) when (checkError is not OperationCanceledException)
            {
                logger.LogError(checkError, "No se pudo persistir incidencia de oferta {IntentoId}; el siguiente ciclo la redetectará", row.IntentoId);
            }
        }
    }

    public async Task<int> RecoverBatchAsync(CancellationToken ct = default)
    {
        // Solo trabajos fallidos y confirmados en la tabla de incidencias; no hay
        // leases de despacho ni operaciones parcialmente confirmadas recuperables.
        var rows = await db.Database.SqlQuery<OfferRow>($"""
            SELECT i."EmpresaId", i."SolicitudId", i."Id" AS "IntentoId"
            FROM incidencias_worker a JOIN intentos_despacho i
              ON i."EmpresaId" = a."EmpresaId" AND i."Id" = a."ReferenciaId"
            WHERE a."Tipo" = 'VencimientoFallido' AND a."AtendidaEn" IS NULL
              AND a."Intentos" < 5 AND a."ProximoIntento" <= clock_timestamp()
              AND i."Estado" = 'Ofertado' AND i."VenceEn" <= clock_timestamp()
            ORDER BY a."ProximoIntento", i."Id" LIMIT {options.BatchSize}
            """).ToListAsync(ct);
        foreach (var row in rows) await ExpireAsync(row, ct);
        return rows.Count;
    }

    public async Task<int> ReconcileBatchAsync(CancellationToken ct = default)
    {
        // NO interpretar la edad de una solicitud Pendiente como abandono. Solo
        // contradicciones observables entre estados/relaciones generan alertas.
        var rows = await db.Database.SqlQuery<AlertRow>($"""
            SELECT s."EmpresaId", s."Id" AS "ReferenciaId", 'EstadoIncoherente' AS "Tipo"
            FROM solicitudes s
            WHERE (
                (s."Estado" = 'Ofertada' AND NOT EXISTS
                    (SELECT 1 FROM intentos_despacho i WHERE i."EmpresaId" = s."EmpresaId" AND i."SolicitudId" = s."Id" AND i."Estado" = 'Ofertado'))
                OR (s."Estado" <> 'Ofertada' AND EXISTS
                    (SELECT 1 FROM intentos_despacho i WHERE i."EmpresaId" = s."EmpresaId" AND i."SolicitudId" = s."Id" AND i."Estado" = 'Ofertado'))
                OR (s."Estado" IN ('Asignada','EnViaje','Finalizada') AND NOT EXISTS
                    (SELECT 1 FROM viajes v WHERE v."Id" = s."ViajeId" AND v."EmpresaId" = s."EmpresaId"
                      AND v."SolicitudId" = s."Id" AND
                        ((s."Estado" = 'Asignada' AND v."Estado" = 'Asignado') OR
                         (s."Estado" = 'EnViaje' AND v."Estado" = 'EnCurso') OR
                         (s."Estado" = 'Finalizada' AND v."Estado" = 'Finalizado'))))
                OR (s."Estado" NOT IN ('Asignada','EnViaje','Finalizada') AND s."ViajeId" IS NOT NULL)
                OR (s."Estado" NOT IN ('Asignada','EnViaje') AND EXISTS
                    (SELECT 1 FROM viajes v WHERE v."EmpresaId" = s."EmpresaId" AND v."SolicitudId" = s."Id" AND v."Estado" IN ('Asignado','EnCurso')))
              )
              AND NOT EXISTS (SELECT 1 FROM incidencias_worker a WHERE a."EmpresaId" = s."EmpresaId"
                  AND a."ReferenciaId" = s."Id" AND a."Tipo" = 'EstadoIncoherente' AND a."AtendidaEn" IS NULL)
            ORDER BY s."CreadaEn", s."Id" LIMIT {options.BatchSize}
            """).ToListAsync(ct);
        foreach (var row in rows)
        {
            // El SELECT ve solo transacciones confirmadas. Una carrera posterior puede
            // requerir que el operador cierre la alerta tras revisar la versión actual.
            await RecordAlertAsync(row.EmpresaId, row.Tipo, row.ReferenciaId, false, ct);
            logger.LogError("Incidencia persistente {Tipo} en solicitud {SolicitudId} empresa {EmpresaId}", row.Tipo, row.ReferenciaId, row.EmpresaId);
        }
        var incomplete = await db.Database.SqlQuery<AlertRow>($"""
            SELECT o."EmpresaId", o."SolicitudId" AS "ReferenciaId", 'OperacionIncompleta' AS "Tipo"
            FROM operaciones_despacho o
            WHERE (o."Estado" IS NULL OR o."Fecha" IS NULL)
              AND NOT EXISTS (SELECT 1 FROM incidencias_worker a WHERE a."EmpresaId" = o."EmpresaId"
                  AND a."ReferenciaId" = o."SolicitudId" AND a."Tipo" = 'OperacionIncompleta' AND a."AtendidaEn" IS NULL)
            ORDER BY o."SolicitudId" LIMIT {options.BatchSize}
            """).ToListAsync(ct);
        foreach (var row in incomplete)
        {
            await RecordAlertAsync(row.EmpresaId, row.Tipo, row.ReferenciaId, false, ct);
            logger.LogError("Operación confirmada incompleta: solicitud {SolicitudId} empresa {EmpresaId}; requiere intervención", row.ReferenciaId, row.EmpresaId);
        }
        return rows.Count + incomplete.Count;
    }

    private Task<int> RecordAlertAsync(Guid empresa, string tipo, Guid id, bool retry, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO incidencias_worker ("EmpresaId","Tipo","ReferenciaId","PrimeraDeteccion","UltimaDeteccion","ProximoIntento","Intentos")
            VALUES ({empresa},{tipo},{id},clock_timestamp(),clock_timestamp(),
                clock_timestamp() + interval '10 seconds',1)
            ON CONFLICT ("EmpresaId","Tipo","ReferenciaId") DO UPDATE
            SET "UltimaDeteccion" = clock_timestamp(),
                "Intentos" = CASE WHEN {retry} THEN LEAST(incidencias_worker."Intentos" + 1, 5) ELSE incidencias_worker."Intentos" END,
                "ProximoIntento" = clock_timestamp() + (CASE WHEN {retry} THEN LEAST(incidencias_worker."Intentos" + 1, 5) ELSE 1 END * interval '10 seconds'),
                "AtendidaEn" = NULL
            """, ct);

    private Task<int> ResolveAlertAsync(Guid empresa, string tipo, Guid id, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE incidencias_worker SET "AtendidaEn" = clock_timestamp()
            WHERE "EmpresaId" = {empresa} AND "Tipo" = {tipo} AND "ReferenciaId" = {id} AND "AtendidaEn" IS NULL
            """, ct);
}

public abstract class PeriodicWorker(IServiceScopeFactory scopes, WorkerOptions options, ILogger logger) : BackgroundService
{
    protected abstract Task<int> ProcessAsync(WorkerProcessor processor, CancellationToken ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Interval);
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var processed = await ProcessAsync(scope.ServiceProvider.GetRequiredService<WorkerProcessor>(), stoppingToken);
                if (processed > 0) logger.LogInformation("{Worker} procesó {Count} registros", GetType().Name, processed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "{Worker} falló; próximo ciclo periódico", GetType().Name); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

public sealed class OfferExpirationWorker(IServiceScopeFactory scopes, WorkerOptions options, ILogger<OfferExpirationWorker> logger)
    : PeriodicWorker(scopes, options, logger)
{
    protected override Task<int> ProcessAsync(WorkerProcessor processor, CancellationToken ct) => processor.ExpireBatchAsync(ct);
}
public sealed class RecoveryWorker(IServiceScopeFactory scopes, WorkerOptions options, ILogger<RecoveryWorker> logger)
    : PeriodicWorker(scopes, options, logger)
{
    protected override Task<int> ProcessAsync(WorkerProcessor processor, CancellationToken ct) => processor.RecoverBatchAsync(ct);
}
public sealed class ReconciliationWorker(IServiceScopeFactory scopes, WorkerOptions options, ILogger<ReconciliationWorker> logger)
    : PeriodicWorker(scopes, options, logger)
{
    protected override Task<int> ProcessAsync(WorkerProcessor processor, CancellationToken ct) => processor.ReconcileBatchAsync(ct);
}

public static class WorkerRegistration
{
    public static IServiceCollection AddRadioMovilWorkers(this IServiceCollection services, WorkerOptions options)
    {
        if (options.ActorId == Guid.Empty || options.BatchSize is < 1 or > 100 || options.Interval < TimeSpan.FromSeconds(1))
            throw new ArgumentException("Configuración de Workers inválida.", nameof(options));
        return services.AddSingleton(options).AddScoped<WorkerProcessor>()
            .AddHostedService<OfferExpirationWorker>().AddHostedService<RecoveryWorker>().AddHostedService<ReconciliationWorker>();
    }
}
