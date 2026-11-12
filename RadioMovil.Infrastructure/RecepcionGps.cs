using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using NetTopologySuite.Geometries;
using RadioMovil.Domain;

namespace RadioMovil.Infrastructure;

/// <summary>Parámetros de AGENTS.md §7, configurables (sección "Gps").</summary>
public sealed class GpsOptions
{
    public int RecienteSegundos { get; init; } = 30;
    public int DesactualizadoSegundos { get; init; } = 90;
    public int IntervaloMinimoSegundos { get; init; } = 3;
    public int MaxAntiguedadCapturaSegundos { get; init; } = 120;
    public int ToleranciaFuturoSegundos { get; init; } = 10;
    public double PrecisionMaximaMetros { get; init; } = 5000;
}

public enum EstadoGps { Reciente, Desactualizado, SinSenal }

public sealed class GpsSinTurnoException() : Exception("No tienes un turno abierto: la ubicación solo se recibe durante el turno.");
public sealed class GpsFrecuenciaException(int reintentarEnSegundos) : Exception("Envío demasiado frecuente.")
{
    public int ReintentarEnSegundos { get; } = reintentarEnSegundos;
}

public sealed record ResultadoGps(bool Aceptada, string? Motivo, DateTimeOffset RecibidaEn, EstadoGps Estado, int IntervaloMinimoSegundos);
public sealed record UbicacionVista(Guid ConductorId, string Nombre, Guid TurnoId, string Patente, double? Latitud, double? Longitud,
    double? PrecisionMetros, DateTimeOffset? CapturadaEn, DateTimeOffset? RecibidaEn, int? AntiguedadSegundos, EstadoGps Estado);
public sealed record ConductorPropioVista(Guid ConductorId, string Nombre, EstadoConductor Estado, string? Ocupacion, TurnoActivoVista? Turno,
    EstadoGps EstadoGps, DateTimeOffset? UltimaRecepcion, int IntervaloEnvioSegundos, Guid? CarreraActivaId = null);

/// <summary>
/// Recepción de coordenadas REALES del conductor. Solo con turno abierto (bloqueado FOR SHARE frente al cierre),
/// fecha de recepción del reloj de PostgreSQL, sin aceptar capturas futuras, viejas ni fuera de orden.
/// Una repetición de la misma captura no cambia nada: es idempotente por (conductor, CapturadaEn).
/// </summary>
public sealed class RecepcionGps(RadioMovilDbContext db, GpsOptions? options = null, IDespachoGuard? guard = null)
{
    private readonly GpsOptions opciones = options ?? new GpsOptions();

    public static EstadoGps Clasificar(DateTimeOffset? recibida, DateTimeOffset ahora, GpsOptions o)
    {
        if (recibida is null) return EstadoGps.SinSenal;
        var edad = (ahora - recibida.Value).TotalSeconds;
        return edad <= o.RecienteSegundos ? EstadoGps.Reciente : edad <= o.DesactualizadoSegundos ? EstadoGps.Desactualizado : EstadoGps.SinSenal;
    }

    public void Validar(double latitud, double longitud, double precisionMetros)
    {
        if (!double.IsFinite(latitud) || latitud is < -90 or > 90 || !double.IsFinite(longitud) || longitud is < -180 or > 180)
            throw new ErrorDominio("Coordenadas fuera de rango.");
        if (!double.IsFinite(precisionMetros) || precisionMetros <= 0 || precisionMetros > opciones.PrecisionMaximaMetros)
            throw new ErrorDominio($"La precisión debe estar entre 0 y {opciones.PrecisionMaximaMetros} metros.");
    }

    public async Task<ResultadoGps> RegistrarAsync(Guid empresaId, Guid actorId, Guid conductorId, double latitud, double longitud,
        double precisionMetros, DateTimeOffset capturadaEn, CancellationToken ct = default)
    {
        Identidad.Exigir(empresaId, nameof(empresaId)); Identidad.Exigir(actorId, nameof(actorId)); Identidad.Exigir(conductorId, nameof(conductorId));
        Validar(latitud, longitud, precisionMetros);
        // PostgreSQL guarda microsegundos: normalizar para que la misma captura se reconozca como repetida.
        capturadaEn = new DateTimeOffset(capturadaEn.UtcTicks - capturadaEn.UtcTicks % 10, TimeSpan.Zero);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (guard is not null) await guard.AuthorizeAsync(empresaId, actorId, GestionFlota.OperacionConductorPropio, conductorId, ct);
        // FOR SHARE: un cierre de turno concurrente (FOR UPDATE) espera, o este envío ve el turno ya cerrado.
        var turnoId = await db.Database.SqlQuery<Guid>($"""
            SELECT "Id" AS "Value" FROM turnos WHERE "ConductorId" = {conductorId} AND "EmpresaId" = {empresaId} AND "Fin" IS NULL FOR SHARE
            """).SingleOrDefaultAsync(ct);
        if (turnoId == Guid.Empty) throw new GpsSinTurnoException();
        var ahora = await AhoraAsync(tx, ct);
        if (capturadaEn > ahora.AddSeconds(opciones.ToleranciaFuturoSegundos)) throw new ErrorDominio("La hora de captura está en el futuro; revisa el reloj del teléfono.");
        if (capturadaEn < ahora.AddSeconds(-opciones.MaxAntiguedadCapturaSegundos)) throw new ErrorDominio("La ubicación es demasiado antigua para considerarse actual.");

        var ultima = await db.Ubicaciones.FromSqlInterpolated($"SELECT * FROM ubicaciones_conductor WHERE \"ConductorId\" = {conductorId} FOR UPDATE")
            .SingleOrDefaultAsync(ct);
        if (ultima is not null && ultima.EmpresaId == empresaId && capturadaEn <= ultima.CapturadaEn)
        {
            await tx.CommitAsync(ct);
            var motivo = capturadaEn == ultima.CapturadaEn ? "Repetida" : "MasAntiguaQueLaUltima";
            return new(false, motivo, ultima.RegistradoEn, Clasificar(ultima.RegistradoEn, ahora, opciones), opciones.IntervaloMinimoSegundos);
        }
        if (ultima is not null && (ahora - ultima.RegistradoEn).TotalSeconds < opciones.IntervaloMinimoSegundos)
            throw new GpsFrecuenciaException((int)Math.Ceiling(opciones.IntervaloMinimoSegundos - (ahora - ultima.RegistradoEn).TotalSeconds));

        var punto = new Point(longitud, latitud) { SRID = 4326 };
        if (ultima is null)
            db.Ubicaciones.Add(new UbicacionConductor { ConductorId = conductorId, EmpresaId = empresaId, TurnoId = turnoId, Posicion = punto,
                PrecisionMetros = precisionMetros, CapturadaEn = capturadaEn, RegistradoEn = ahora });
        else
        {
            ultima.EmpresaId = empresaId; ultima.TurnoId = turnoId; ultima.Posicion = punto;
            ultima.PrecisionMetros = precisionMetros; ultima.CapturadaEn = capturadaEn; ultima.RegistradoEn = ahora;
        }
        db.UbicacionesHistorial.Add(new UbicacionHistorial { EmpresaId = empresaId, ConductorId = conductorId, TurnoId = turnoId, Posicion = punto,
            PrecisionMetros = precisionMetros, CapturadaEn = capturadaEn, RecibidaEn = ahora });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(true, null, ahora, EstadoGps.Reciente, opciones.IntervaloMinimoSegundos);
    }

    /// <summary>Última ubicación de cada conductor en turno. Una ubicación de un turno anterior no cuenta.</summary>
    public async Task<(DateTimeOffset ahora, IReadOnlyList<UbicacionVista> items)> UltimasAsync(Guid empresaId, CancellationToken ct = default)
    {
        Identidad.Exigir(empresaId, nameof(empresaId));
        var ahora = await AhoraAsync(null, ct);
        var rows = await (from t in db.Turnos.AsNoTracking()
                          join c in db.Conductores.AsNoTracking() on t.ConductorId equals c.Id
                          join v in db.Vehiculos.AsNoTracking() on t.VehiculoId equals v.Id
                          from u in db.Ubicaciones.AsNoTracking().Where(u => u.ConductorId == t.ConductorId && u.TurnoId == t.Id).DefaultIfEmpty()
                          where t.EmpresaId == empresaId && t.Fin == null
                          orderby c.Nombre, c.Id
                          select new { t.ConductorId, c.Nombre, TurnoId = t.Id, v.Patente, u }).Take(1000).ToListAsync(ct);
        return (ahora, rows.Select(r =>
        {
            var recibida = r.u?.RegistradoEn;
            return new UbicacionVista(r.ConductorId, r.Nombre, r.TurnoId, r.Patente, r.u?.Posicion.Y, r.u?.Posicion.X, r.u?.PrecisionMetros,
                r.u?.CapturadaEn, recibida, recibida is null ? null : (int)Math.Max(0, (ahora - recibida.Value).TotalSeconds), Clasificar(recibida, ahora, opciones));
        }).ToList());
    }

    public async Task<ConductorPropioVista?> PropioAsync(Guid empresaId, Guid conductorId, GestionFlota flota, CancellationToken ct = default)
    {
        var conductor = (await flota.ConductoresAsync(empresaId, ct)).SingleOrDefault(c => c.Id == conductorId);
        if (conductor is null) return null;
        var ahora = await AhoraAsync(null, ct);
        DateTimeOffset? recibida = conductor.Turno is { } turno
            ? await db.Ubicaciones.AsNoTracking().Where(u => u.ConductorId == conductorId && u.TurnoId == turno.Id).Select(u => (DateTimeOffset?)u.RegistradoEn).SingleOrDefaultAsync(ct)
            : null;
        // Carrera asignada (estado persistido), para que la app solo consulte /carreras cuando hay algo.
        var carrera = await db.Viajes.AsNoTracking().Where(v => v.EmpresaId == empresaId && v.ConductorId == conductorId &&
            Viaje.Activos.Contains(v.Estado)).Select(v => (Guid?)v.Id).SingleOrDefaultAsync(ct);
        return new(conductor.Id, conductor.Nombre, conductor.Estado, conductor.Ocupacion, conductor.Turno,
            Clasificar(recibida, ahora, opciones), recibida, Math.Max(opciones.IntervaloMinimoSegundos, 5), carrera);
    }

    private async Task<DateTimeOffset> AhoraAsync(IDbContextTransaction? tx, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        var opened = connection.State != System.Data.ConnectionState.Open;
        if (opened) await connection.OpenAsync(ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = tx?.GetDbTransaction();
            command.CommandText = "SELECT clock_timestamp()";
            var time = (DateTime)(await command.ExecuteScalarAsync(ct) ?? throw new InvalidOperationException("Reloj no disponible."));
            return new DateTimeOffset(time.ToUniversalTime());
        }
        finally { if (opened) await connection.CloseAsync(); }
    }
}
