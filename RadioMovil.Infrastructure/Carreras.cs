using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using NetTopologySuite.Geometries;
using RadioMovil.Domain;

namespace RadioMovil.Infrastructure;

public sealed class CarreraNoEncontradaException : Exception;

// Contratos nuevos (app Flutter y central): estados como TEXTO estable ("Reciente", "Asignado"…), no como números.
public sealed record CandidatoCercano(Guid ConductorId, string Nombre, string Patente, string EstadoGps, int? AntiguedadSegundos,
    double? Latitud, double? Longitud, double? DistanciaMetros);
public sealed record CandidatosCercanos(Guid SolicitudId, string EstadoSolicitud, double? RecogidaLatitud, double? RecogidaLongitud,
    IReadOnlyList<CandidatoCercano> Items);
public sealed record CarreraActiva(Guid ViajeId, Guid SolicitudId, long OrdenRecepcion, string Origen, string? Destino, string Estado,
    DateTimeOffset AsignadoEn, DateTimeOffset? RecibidaEn, Guid ConductorId, string ConductorNombre, string? Patente, DateTimeOffset? UltimoPasoEn);
public sealed record CarreraConductor(Guid ViajeId, Guid SolicitudId, string Estado, DateTimeOffset AsignadoEn, DateTimeOffset? RecibidaEn,
    string Origen, double? RecogidaLatitud, double? RecogidaLongitud, string? Destino, double? DestinoLatitud, double? DestinoLongitud,
    string? Observaciones, string? NombrePasajero, string? TelefonoPasajero);

/// <summary>
/// Lecturas de la asignación directa (decisión 2026-11-10) y la confirmación de recepción del conductor.
/// La distancia es la geodésica de PostGIS entre el GPS REAL del turno actual y el punto marcado por la central;
/// nunca se estima sin ambos puntos ni se presenta GPS «Sin señal» como posición actual.
/// </summary>
public sealed class Carreras(RadioMovilDbContext db, GpsOptions? options = null, IDespachoGuard? guard = null)
{
    private readonly GpsOptions opciones = options ?? new GpsOptions();

    private sealed record FilaCandidato(Guid ConductorId, string Nombre, string Patente, DateTimeOffset? RegistradoEn,
        double? Latitud, double? Longitud, double? DistanciaMetros);

    public async Task<CandidatosCercanos?> CandidatosCercanosAsync(Guid empresaId, Guid solicitudId, CancellationToken ct = default)
    {
        Identidad.Exigir(empresaId, nameof(empresaId)); Identidad.Exigir(solicitudId, nameof(solicitudId));
        var solicitud = await db.Solicitudes.AsNoTracking().Where(s => s.EmpresaId == empresaId && s.Id == solicitudId)
            .Select(s => new { s.Estado, Punto = EF.Property<Point?>(s, "PuntoRecogida") }).SingleOrDefaultAsync(ct);
        if (solicitud is null) return null;
        var ahora = await AhoraAsync(ct);
        // Disponibles = turno abierto, sin oferta ni viaje activos (misma definición que el estado «Disponible» de Flota).
        var filas = await db.Database.SqlQuery<FilaCandidato>($"""
            SELECT c."Id" AS "ConductorId", c."Nombre", v."Patente", u."RegistradoEn",
                   ST_Y(u."Posicion") AS "Latitud", ST_X(u."Posicion") AS "Longitud",
                   CASE WHEN s."PuntoRecogida" IS NULL OR u."Posicion" IS NULL THEN NULL
                        ELSE ST_Distance(u."Posicion"::geography, s."PuntoRecogida"::geography) END AS "DistanciaMetros"
            FROM turnos t
            JOIN conductores c ON c."Id" = t."ConductorId" AND c."EmpresaId" = t."EmpresaId"
            JOIN vehiculos v ON v."Id" = t."VehiculoId" AND v."EmpresaId" = t."EmpresaId"
            JOIN solicitudes s ON s."Id" = {solicitudId} AND s."EmpresaId" = {empresaId}
            LEFT JOIN ubicaciones_conductor u ON u."ConductorId" = t."ConductorId" AND u."TurnoId" = t."Id"
            WHERE t."EmpresaId" = {empresaId} AND t."Fin" IS NULL
              AND NOT EXISTS (SELECT 1 FROM intentos_despacho i WHERE i."ConductorId" = t."ConductorId" AND i."Estado" = 'Ofertado')
              AND NOT EXISTS (SELECT 1 FROM viajes vj WHERE vj."ConductorId" = t."ConductorId" AND vj."Estado" IN ('Asignado', 'EnCamino', 'EnPuerta', 'EnCurso'))
            """).ToListAsync(ct);
        var items = filas.Select(f =>
        {
            var estado = RecepcionGps.Clasificar(f.RegistradoEn, ahora, opciones);
            var visible = estado != EstadoGps.SinSenal;
            return (estado, item: new CandidatoCercano(f.ConductorId, f.Nombre, f.Patente, estado.ToString(),
                f.RegistradoEn is { } r ? (int)Math.Max(0, (ahora - r).TotalSeconds) : null,
                visible ? f.Latitud : null, visible ? f.Longitud : null, visible ? f.DistanciaMetros : null));
        })
        // Primero GPS reciente por distancia, luego desactualizado por distancia, al final sin señal por nombre.
        .OrderBy(x => x.estado).ThenBy(x => x.item.DistanciaMetros ?? double.MaxValue)
        .ThenBy(x => x.item.Nombre, StringComparer.CurrentCulture).ThenBy(x => x.item.ConductorId)
        .Select(x => x.item).ToList();
        return new(solicitudId, solicitud.Estado.ToString(), solicitud.Punto?.Y, solicitud.Punto?.X, items);
    }

    public async Task<IReadOnlyList<CarreraActiva>> ActivasAsync(Guid empresaId, CancellationToken ct = default)
    {
        Identidad.Exigir(empresaId, nameof(empresaId));
        var rows = await (from v in db.Viajes.AsNoTracking()
                          where v.EmpresaId == empresaId && Viaje.Activos.Contains(v.Estado)
                          join s in db.Solicitudes.AsNoTracking() on v.SolicitudId equals s.Id
                          join c in db.Conductores.AsNoTracking() on v.ConductorId equals c.Id
                          orderby v.AsignadoEn, v.Id
                          select new
                          {
                              v.Id, v.SolicitudId, Orden = EF.Property<long>(s, "OrdenRecepcion"), s.Origen, Destino = EF.Property<string?>(s, "Destino"),
                              v.Estado, v.AsignadoEn, v.RecibidaEn, v.ConductorId, c.Nombre,
                              Patente = db.Turnos.Where(t => t.ConductorId == v.ConductorId && t.Fin == null)
                                  .Join(db.Vehiculos, t => t.VehiculoId, x => x.Id, (t, x) => x.Patente).FirstOrDefault(),
                              UltimoPasoEn = db.ViajeEventos.Where(e => e.ViajeId == v.Id).Max(e => (DateTimeOffset?)e.En)
                          }).ToListAsync(ct);
        return rows.Select(r => new CarreraActiva(r.Id, r.SolicitudId, r.Orden, r.Origen, r.Destino, r.Estado.ToString(), r.AsignadoEn, r.RecibidaEn,
            r.ConductorId, r.Nombre, r.Patente, r.UltimoPasoEn)).ToList();
    }

    /// <summary>Carreras activas del conductor (Asignado, EnCamino, EnPuerta, EnCurso): lo que la app recupera al abrir o reconectar.</summary>
    public async Task<IReadOnlyList<CarreraConductor>> DelConductorAsync(Guid empresaId, Guid conductorId, CancellationToken ct = default)
    {
        Identidad.Exigir(empresaId, nameof(empresaId)); Identidad.Exigir(conductorId, nameof(conductorId));
        var rows = await (from v in db.Viajes.AsNoTracking()
                          where v.EmpresaId == empresaId && v.ConductorId == conductorId && Viaje.Activos.Contains(v.Estado)
                          join s in db.Solicitudes.AsNoTracking() on v.SolicitudId equals s.Id
                          orderby v.AsignadoEn, v.Id
                          select new
                          {
                              v.Id, v.SolicitudId, v.Estado, v.AsignadoEn, v.RecibidaEn, s.Origen,
                              Recogida = EF.Property<Point?>(s, "PuntoRecogida"), Destino = EF.Property<string?>(s, "Destino"),
                              PuntoDestino = EF.Property<Point?>(s, "PuntoDestino"), Observaciones = EF.Property<string?>(s, "Observaciones"),
                              Nombre = EF.Property<string?>(s, "NombrePasajero"), Telefono = EF.Property<string?>(s, "TelefonoPasajero")
                          }).ToListAsync(ct);
        return rows.Select(r => new CarreraConductor(r.Id, r.SolicitudId, r.Estado.ToString(), r.AsignadoEn, r.RecibidaEn, r.Origen,
            r.Recogida?.Y, r.Recogida?.X, r.Destino, r.PuntoDestino?.Y, r.PuntoDestino?.X, r.Observaciones, r.Nombre, r.Telefono)).ToList();
    }

    /// <summary>
    /// El conductor confirma que VIO la orden. Es entrega, no aceptación: no cambia el estado del viaje ni de la solicitud.
    /// Idempotente sin clave: solo la primera confirmación fija RecibidaEn (reloj de PostgreSQL).
    /// </summary>
    public async Task<(DateTimeOffset RecibidaEn, bool Repetida)> ConfirmarRecepcionAsync(Guid empresaId, Guid actorId, Guid conductorId, Guid viajeId, CancellationToken ct = default)
    {
        Identidad.Exigir(empresaId, nameof(empresaId)); Identidad.Exigir(actorId, nameof(actorId));
        Identidad.Exigir(conductorId, nameof(conductorId)); Identidad.Exigir(viajeId, nameof(viajeId));
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (guard is not null) await guard.AuthorizeAsync(empresaId, actorId, GestionFlota.OperacionConductorPropio, conductorId, ct);
        var viaje = await db.Viajes.FromSqlInterpolated($"""
            SELECT * FROM viajes WHERE "Id" = {viajeId} AND "EmpresaId" = {empresaId} AND "ConductorId" = {conductorId} FOR UPDATE
            """).SingleOrDefaultAsync(ct) ?? throw new CarreraNoEncontradaException();
        var nueva = viaje.ConfirmarRecepcion(await AhoraAsync(ct, tx));
        if (nueva)
        {
            db.AuditoriaAcceso.Add(new AuditoriaAcceso { EmpresaId = empresaId, UsuarioId = actorId, Tipo = "OrdenRecibida", ObjetoId = viajeId, Fecha = viaje.RecibidaEn!.Value });
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        return (viaje.RecibidaEn!.Value, !nueva);
    }

    private async Task<DateTimeOffset> AhoraAsync(CancellationToken ct, IDbContextTransaction? tx = null)
    {
        var connection = db.Database.GetDbConnection();
        var opened = connection.State != System.Data.ConnectionState.Open;
        if (opened) await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var command = connection.CreateCommand();
            if (tx is not null) command.Transaction = tx.GetDbTransaction();
            command.CommandText = "SELECT clock_timestamp()";
            var value = (DateTime)(await command.ExecuteScalarAsync(ct) ?? throw new InvalidOperationException("Reloj no disponible."));
            return new DateTimeOffset(value.ToUniversalTime());
        }
        finally { if (opened) await db.Database.CloseConnectionAsync(); }
    }
}
