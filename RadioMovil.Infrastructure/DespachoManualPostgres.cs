using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using RadioMovil.Application;
using RadioMovil.Domain;

namespace RadioMovil.Infrastructure;

public sealed class DespachoConflictoException(string message) : Exception(message);
public sealed class SolicitudAjenaONoEncontradaException : Exception;

// El índice único de operaciones serializa reintentos; la fila de solicitud serializa
// transiciones. Los índices parciales y triggers son la última defensa entre solicitudes.
public interface IDespachoGuard
{
    Task AuthorizeAsync(Guid empresaId, Guid actorId, string operation, Guid? target, CancellationToken ct);
}

public sealed class DespachoManualPostgres(RadioMovilDbContext db, IDespachoGuard? guard = null, GpsOptions? gps = null) : IDespachoManual
{
    public async Task<IReadOnlyList<CandidatoDespacho>> CandidatosAsync(Guid empresaId, CancellationToken ct = default)
    {
        Identidad.Exigir(empresaId, nameof(empresaId));
        // Ubicación verificada SOLO con GPS real reciente del turno actual; nunca se simula ni se calcula ETA aquí.
        var now = DateTimeOffset.UtcNow;
        var reciente = now.AddSeconds(-(gps ?? new GpsOptions()).RecienteSegundos);
        return await db.Conductores.AsNoTracking().Where(c => c.EmpresaId == empresaId &&
                db.Turnos.Any(t => t.EmpresaId == empresaId && t.ConductorId == c.Id && t.Inicio <= now && (t.Fin == null || t.Fin > now)) &&
                !db.Intentos.Any(i => i.EmpresaId == empresaId && i.ConductorId == c.Id && i.Estado == EstadoIntento.Ofertado) &&
                !db.Viajes.Any(v => v.EmpresaId == empresaId && v.ConductorId == c.Id && Viaje.Activos.Contains(v.Estado)))
            .OrderBy(c => c.Nombre).ThenBy(c => c.Id)
            .Select(c => new CandidatoDespacho(c.Id, c.Nombre, db.Ubicaciones.Any(u => u.ConductorId == c.Id && u.RegistradoEn >= reciente &&
                db.Turnos.Any(t => t.Id == u.TurnoId && t.Fin == null)))).ToListAsync(ct);
    }

    public Task<ResultadoDespacho> AsignarAsync(Guid empresaId, Guid solicitudId, Guid conductorId, Guid actorId, string clave, CancellationToken ct = default) =>
        ExecuteAsync(empresaId, solicitudId, actorId, "asignar", clave, new { conductorId, actorId }, conductorId, null, ct);

    public Task<ResultadoDespacho> OfrecerAsync(Guid empresaId, Guid solicitudId, Guid conductorId, Guid actorId, string clave, CancellationToken ct = default) =>
        ExecuteAsync(empresaId, solicitudId, actorId, "ofrecer", clave, new { conductorId, actorId }, conductorId, null, ct);

    public Task<ResultadoDespacho> AceptarAsync(Guid empresaId, Guid solicitudId, Guid intentoId, Guid actorId, string clave, CancellationToken ct = default) =>
        ExecuteAsync(empresaId, solicitudId, actorId, "aceptar", clave, new { intentoId, actorId }, intentoId, null, ct);

    public Task<ResultadoDespacho> RechazarAsync(Guid empresaId, Guid solicitudId, Guid intentoId, Guid actorId, string motivo, string clave, CancellationToken ct = default) =>
        ExecuteAsync(empresaId, solicitudId, actorId, "rechazar", clave, new { intentoId, actorId, motivo = Motivo(motivo) }, intentoId, Motivo(motivo), ct);

    public Task<ResultadoDespacho> VencerAsync(Guid empresaId, Guid solicitudId, Guid intentoId, Guid actorId, string clave, CancellationToken ct = default) =>
        ExecuteAsync(empresaId, solicitudId, actorId, "vencer", clave, new { intentoId, actorId }, intentoId, null, ct);

    public Task<ResultadoDespacho> CancelarClienteAsync(Guid empresaId, Guid solicitudId, Guid actorId, string motivo, string clave, CancellationToken ct = default) =>
        ExecuteAsync(empresaId, solicitudId, actorId, "cancelar_cliente", clave, new { actorId, motivo = Motivo(motivo) }, null, Motivo(motivo), ct);

    public Task<ResultadoDespacho> CancelarConductorAsync(Guid empresaId, Guid solicitudId, Guid viajeId, Guid actorId, string motivo, string clave, CancellationToken ct = default) =>
        ExecuteAsync(empresaId, solicitudId, actorId, "cancelar_conductor", clave, new { viajeId, actorId, motivo = Motivo(motivo) }, viajeId, Motivo(motivo), ct);

    private static string Motivo(string value)
    {
        var reason = Identidad.Texto(value, nameof(value));
        if (reason.Length > 1000) throw new ErrorDominio("Motivo excede 1000 caracteres.");
        return reason;
    }

    private async Task<ResultadoDespacho> ExecuteAsync(Guid empresaId, Guid solicitudId, Guid actorId,
        string ambito, string clave, object contenido, Guid? target, string? motivo, CancellationToken ct)
    {
        Identidad.Exigir(empresaId, nameof(empresaId)); Identidad.Exigir(solicitudId, nameof(solicitudId));
        Identidad.Exigir(actorId, nameof(actorId));
        if (target == Guid.Empty) throw new ErrorDominio("Identificador inválido.");
        if (string.IsNullOrWhiteSpace(clave) || clave.Length > 160) throw new ErrorDominio("Idempotency-Key inválida.");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { solicitudId, contenido }))));
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // La autorización se verifica ANTES de devolver resultados históricos,
        // y se revalida bajo el bloqueo de solicitud en operaciones nuevas.
        if (guard is not null) await guard.AuthorizeAsync(empresaId, actorId, ambito, target, ct);
        var claimed = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO operaciones_despacho ("EmpresaId", "Ambito", "Clave", "Huella", "SolicitudId")
            VALUES ({empresaId}, {ambito}, {clave}, {hash}, {solicitudId})
            ON CONFLICT ("EmpresaId", "Ambito", "Clave") DO NOTHING
            """, ct);
        if (claimed == 0)
        {
            var old = await db.OperacionesDespacho.AsNoTracking().SingleAsync(x => x.EmpresaId == empresaId && x.Ambito == ambito && x.Clave == clave, ct);
            if (old.Huella != hash || old.SolicitudId != solicitudId) throw new DespachoConflictoException("Clave reutilizada con otro contenido.");
            if (old.Estado is null || old.Fecha is null) throw new InvalidOperationException("Resultado de idempotencia incompleto.");
            await tx.CommitAsync(ct);
            return new(old.SolicitudId, old.IntentoId, old.ViajeId, old.Estado.Value, old.Fecha.Value, true);
        }

        // La lectura bloqueante precede a reloj/estado: al despertar tras otra operación
        // nunca se acepta una oferta usando la hora anterior a la espera.
        await using (var lockCommand = db.Database.GetDbConnection().CreateCommand())
        {
            lockCommand.Transaction = tx.GetDbTransaction();
            lockCommand.CommandText = "SELECT \"Id\" FROM solicitudes WHERE \"Id\" = @id AND \"EmpresaId\" = @empresa FOR UPDATE";
            var p1 = lockCommand.CreateParameter(); p1.ParameterName = "id"; p1.Value = solicitudId; lockCommand.Parameters.Add(p1);
            var p2 = lockCommand.CreateParameter(); p2.ParameterName = "empresa"; p2.Value = empresaId; lockCommand.Parameters.Add(p2);
            if (await lockCommand.ExecuteScalarAsync(ct) is null) throw new SolicitudAjenaONoEncontradaException();
        }
        if (guard is not null) await guard.AuthorizeAsync(empresaId, actorId, ambito, target, ct);
        var solicitud = await db.Solicitudes.Include(x => x.Intentos).Include(x => x.Eventos).AsSplitQuery()
            .SingleAsync(x => x.EmpresaId == empresaId && x.Id == solicitudId, ct);
        var now = await DatabaseNowAsync(tx, ct);
        Guid? intentoId = null; Guid? viajeId = null;
        switch (ambito)
        {
            case "asignar":
                // Validación legible; la defensa final (turno abierto, exclusividad por conductor y por solicitud,
                // sin oferta activa) la aplican el trigger y los índices únicos parciales bajo el advisory lock del conductor.
                await ExigirConductorEnTurnoAsync(empresaId, target!.Value, now, ct);
                if (await db.Viajes.AnyAsync(v => v.EmpresaId == empresaId && v.ConductorId == target && Viaje.Activos.Contains(v.Estado), ct) ||
                    await db.Intentos.AnyAsync(i => i.EmpresaId == empresaId && i.ConductorId == target && i.Estado == EstadoIntento.Ofertado, ct))
                    throw new DespachoConflictoException("El conductor ya tiene una carrera activa.");
                var asignado = solicitud.AsignarDirecto(target.Value, now, actorId);
                viajeId = asignado.Id;
                db.Viajes.Add(asignado);
                break;
            case "ofrecer":
                var conductorId = target!.Value;
                await ExigirConductorEnTurnoAsync(empresaId, conductorId, now, ct);
                intentoId = solicitud.Ofrecer(conductorId, now, now.AddSeconds(20), actorId).Id;
                break;
            case "aceptar":
                var viaje = solicitud.Aceptar(target!.Value, now, actorId);
                intentoId = target;
                viajeId = viaje.Id;
                // El trigger de viajes exige que la oferta ya no sea Ofertado.
                // Ambos SaveChanges y el COMMIT pertenecen a la MISMA transacción.
                await db.SaveChangesAsync(ct);
                db.Viajes.Add(viaje);
                break;
            case "rechazar":
                solicitud.Rechazar(target!.Value, now, actorId, motivo!);
                intentoId = target;
                break;
            case "vencer":
                solicitud.Vencer(target!.Value, now, actorId);
                intentoId = target;
                break;
            case "cancelar_cliente":
                var activeTrip = solicitud.ViajeId is Guid currentId
                    ? await db.Viajes.SingleOrDefaultAsync(v => v.Id == currentId && v.EmpresaId == empresaId && v.SolicitudId == solicitudId, ct)
                    : null;
                viajeId = activeTrip?.Id;
                solicitud.CancelarCliente(activeTrip, now, actorId, motivo!);
                break;
            case "cancelar_conductor":
                var trip = await db.Viajes.SingleOrDefaultAsync(v => v.Id == target && v.EmpresaId == empresaId && v.SolicitudId == solicitudId, ct)
                    ?? throw new DespachoConflictoException("Viaje inexistente.");
                solicitud.CancelarConductorAntesDeRecoger(trip, now, actorId, motivo!);
                viajeId = target;
                break;
            default: throw new InvalidOperationException("Operación desconocida.");
        }
        var operation = await db.OperacionesDespacho.SingleAsync(x => x.EmpresaId == empresaId && x.Ambito == ambito && x.Clave == clave, ct);
        operation.IntentoId = intentoId; operation.ViajeId = viajeId;
        operation.Estado = solicitud.Estado; operation.Fecha = now;
        db.Outbox.Add(new EventoOutbox
        {
            Id = Guid.NewGuid(), EmpresaId = empresaId, Tipo = "Despacho." + ambito,
            // "Despacho.asignar" es la orden de servicio a notificar: solo existe si este COMMIT confirma la asignación.
            Contenido = ambito == "asignar"
                ? JsonSerializer.Serialize(new { solicitudId, intentoId, viajeId, conductorId = target, estado = solicitud.Estado.ToString(), fecha = now })
                : JsonSerializer.Serialize(new { solicitudId, intentoId, viajeId, estado = solicitud.Estado.ToString(), fecha = now }),
            CreadoEn = now
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(solicitudId, intentoId, viajeId, solicitud.Estado, now, false);
    }

    private async Task ExigirConductorEnTurnoAsync(Guid empresaId, Guid conductorId, DateTimeOffset now, CancellationToken ct)
    {
        if (!await db.Conductores.AnyAsync(c => c.Id == conductorId && c.EmpresaId == empresaId, ct) ||
            !await db.Turnos.AnyAsync(t => t.EmpresaId == empresaId && t.ConductorId == conductorId && t.Inicio <= now && (t.Fin == null || t.Fin > now), ct))
            throw new DespachoConflictoException("Conductor sin turno activo.");
    }

    private async Task<DateTimeOffset> DatabaseNowAsync(IDbContextTransaction tx, CancellationToken ct)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = tx.GetDbTransaction();
        command.CommandText = "SELECT clock_timestamp()";
        var time = (DateTime)(await command.ExecuteScalarAsync(ct) ?? throw new InvalidOperationException("Reloj no disponible."));
        return new DateTimeOffset(time.ToUniversalTime());
    }
}
