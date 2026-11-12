using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using NetTopologySuite.Geometries;
using RadioMovil.Domain;

namespace RadioMovil.Infrastructure;

public enum PasoViaje { EnCamino, Llegada, Iniciar, Finalizar }
public sealed class CarreraConflictoException(string message) : Exception(message);
public sealed record ResultadoPaso(Guid ViajeId, string Estado, DateTimeOffset En, bool Repetida);
/// <summary>Destino de la ruta según el estado: recogida antes de subir el pasajero, destino durante el recorrido.</summary>
public sealed record DestinoRuta(string HaciaPunto, double? Latitud, double? Longitud);

/// <summary>
/// Pasos del CONDUCTOR sobre su carrera (decisión del responsable 2026-11-10): Ir a recoger → Llegué → Iniciar recorrido → Finalizar.
/// Solo el conductor dueño, con turno abierto, con Idempotency-Key. Bloqueo solicitud → viaje (mismo orden que la cancelación
/// de la central), hora de PostgreSQL y la última posición GPS aceptada de su turno, sin inventarla si no existe.
/// </summary>
public sealed class PasosViaje(RadioMovilDbContext db, IDespachoGuard? guard = null)
{
    private static (EstadoViaje Destino, string Ambito) Datos(PasoViaje paso) => paso switch
    {
        PasoViaje.EnCamino => (EstadoViaje.EnCamino, "viaje_en_camino"),
        PasoViaje.Llegada => (EstadoViaje.EnPuerta, "viaje_llegada"),
        PasoViaje.Iniciar => (EstadoViaje.EnCurso, "viaje_iniciar"),
        PasoViaje.Finalizar => (EstadoViaje.Finalizado, "viaje_finalizar"),
        _ => throw new ArgumentOutOfRangeException(nameof(paso))
    };

    public async Task<ResultadoPaso> AvanzarAsync(Guid empresaId, Guid actorId, Guid conductorId, Guid viajeId, PasoViaje paso,
        string? observaciones, string clave, CancellationToken ct = default)
    {
        Identidad.Exigir(empresaId, nameof(empresaId)); Identidad.Exigir(actorId, nameof(actorId));
        Identidad.Exigir(conductorId, nameof(conductorId)); Identidad.Exigir(viajeId, nameof(viajeId));
        if (string.IsNullOrWhiteSpace(clave) || clave.Length > 160) throw new ErrorDominio("Idempotency-Key inválida.");
        var nota = string.IsNullOrWhiteSpace(observaciones) ? null : observaciones.Trim();
        if (nota is { Length: > 500 }) throw new ErrorDominio("Las observaciones admiten hasta 500 caracteres.");
        if (nota is not null && paso != PasoViaje.Finalizar) throw new ErrorDominio("Las observaciones solo se envían al finalizar.");
        var (destino, ambito) = Datos(paso);
        var huella = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { viajeId, paso = paso.ToString(), nota }))));

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (guard is not null) await guard.AuthorizeAsync(empresaId, actorId, GestionFlota.OperacionConductorPropio, conductorId, ct);
        var reclamada = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO operaciones_flota ("EmpresaId", "Ambito", "Clave", "Huella", "RecursoId", "Fecha")
            VALUES ({empresaId}, {ambito}, {clave}, {huella}, {Guid.Empty}, clock_timestamp())
            ON CONFLICT ("EmpresaId", "Ambito", "Clave") DO NOTHING
            """, ct);
        if (reclamada == 0)
        {
            var previa = await db.OperacionesFlota.AsNoTracking().SingleAsync(x => x.EmpresaId == empresaId && x.Ambito == ambito && x.Clave == clave, ct);
            if (previa.Huella != huella) throw new CarreraConflictoException("Idempotency-Key ya utilizada con otro contenido.");
            var evento = await db.ViajeEventos.AsNoTracking().SingleOrDefaultAsync(e => e.Id == previa.RecursoId && e.EmpresaId == empresaId, ct)
                ?? throw new InvalidOperationException("Resultado de idempotencia incompleto.");
            await tx.CommitAsync(ct);
            return new(evento.ViajeId, evento.Tipo, evento.En, true);
        }

        var solicitudId = await db.Viajes.AsNoTracking().Where(v => v.Id == viajeId && v.EmpresaId == empresaId && v.ConductorId == conductorId)
            .Select(v => (Guid?)v.SolicitudId).SingleOrDefaultAsync(ct) ?? throw new CarreraNoEncontradaException();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT 1 FROM solicitudes WHERE "Id" = {solicitudId} AND "EmpresaId" = {empresaId} FOR UPDATE
            """, ct);
        var solicitud = await db.Solicitudes.Include(x => x.Intentos).Include(x => x.Eventos).AsSplitQuery()
            .SingleAsync(x => x.EmpresaId == empresaId && x.Id == solicitudId, ct);
        var viaje = await db.Viajes.FromSqlInterpolated($"""
            SELECT * FROM viajes WHERE "Id" = {viajeId} AND "EmpresaId" = {empresaId} AND "ConductorId" = {conductorId} FOR UPDATE
            """).SingleAsync(ct);
        if (!viaje.EstaActivo) throw new CarreraConflictoException("La carrera ya está cerrada o fue cancelada por la central.");
        var turno = await db.Turnos.AsNoTracking().Where(t => t.EmpresaId == empresaId && t.ConductorId == conductorId && t.Fin == null)
            .Select(t => (Guid?)t.Id).SingleOrDefaultAsync(ct) ?? throw new CarreraConflictoException("No tienes un turno abierto.");
        var ahora = await AhoraAsync(tx, ct);

        switch (paso)
        {
            case PasoViaje.EnCamino: viaje.IrARecoger(ahora); break;
            case PasoViaje.Llegada: viaje.MarcarLlegada(ahora); break;
            case PasoViaje.Iniciar:
                viaje.IniciarRecorrido(ahora);
                solicitud.IniciarViaje(viajeId, ahora, actorId);
                break;
            case PasoViaje.Finalizar:
                viaje.Finalizar(ahora);
                solicitud.FinalizarViaje(viajeId, ahora, actorId);
                break;
        }

        var gps = await db.Ubicaciones.AsNoTracking().Where(u => u.ConductorId == conductorId && u.EmpresaId == empresaId && u.TurnoId == turno)
            .Select(u => new { u.Posicion, u.PrecisionMetros, u.RegistradoEn }).SingleOrDefaultAsync(ct);
        var registro = new ViajeEvento
        {
            Id = Guid.NewGuid(), EmpresaId = empresaId, ViajeId = viajeId, Tipo = destino.ToString(), En = ahora, ActorId = actorId,
            Posicion = gps?.Posicion is { } p ? new Point(p.X, p.Y) { SRID = 4326 } : null,
            PrecisionMetros = gps?.PrecisionMetros, GpsRegistradoEn = gps?.RegistradoEn, Observaciones = nota
        };
        db.ViajeEventos.Add(registro);
        var operacion = await db.OperacionesFlota.SingleAsync(x => x.EmpresaId == empresaId && x.Ambito == ambito && x.Clave == clave, ct);
        operacion.RecursoId = registro.Id; operacion.Fecha = ahora;
        db.AuditoriaAcceso.Add(new AuditoriaAcceso { EmpresaId = empresaId, UsuarioId = actorId, Fecha = ahora, Tipo = "Viaje" + destino, ObjetoId = viajeId });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(viajeId, registro.Tipo, ahora, false);
    }

    /// <summary>Hacia dónde va la ruta del conductor ahora mismo. Null si la carrera no es suya o ya no está activa.</summary>
    public async Task<DestinoRuta?> DestinoRutaAsync(Guid empresaId, Guid conductorId, Guid viajeId, CancellationToken ct = default)
    {
        var fila = await (from v in db.Viajes.AsNoTracking()
                          where v.Id == viajeId && v.EmpresaId == empresaId && v.ConductorId == conductorId && Viaje.Activos.Contains(v.Estado)
                          join s in db.Solicitudes.AsNoTracking() on v.SolicitudId equals s.Id
                          select new { v.Estado, Recogida = EF.Property<Point?>(s, "PuntoRecogida"), Destino = EF.Property<Point?>(s, "PuntoDestino") })
            .SingleOrDefaultAsync(ct);
        if (fila is null) return null;
        var haciaDestino = fila.Estado == EstadoViaje.EnCurso;
        var punto = haciaDestino ? fila.Destino : fila.Recogida;
        return new(haciaDestino ? "Destino" : "Recogida", punto?.Y, punto?.X);
    }

    private static async Task<DateTimeOffset> AhoraAsync(IDbContextTransaction tx, CancellationToken ct)
    {
        await using var command = tx.GetDbTransaction().Connection!.CreateCommand();
        command.Transaction = tx.GetDbTransaction();
        command.CommandText = "SELECT clock_timestamp()";
        var time = (DateTime)(await command.ExecuteScalarAsync(ct) ?? throw new InvalidOperationException("Reloj no disponible."));
        return new DateTimeOffset(time.ToUniversalTime());
    }
}
