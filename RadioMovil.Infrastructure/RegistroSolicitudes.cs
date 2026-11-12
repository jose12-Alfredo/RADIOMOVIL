using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Diagnostics;
using RadioMovil.Domain;
using NetTopologySuite.Geometries;

namespace RadioMovil.Infrastructure;

public sealed class ConflictoIdempotenciaException : Exception
{
    public ConflictoIdempotenciaException() : base("Idempotency-Key ya corresponde a otra solicitud.") { }
}
public sealed record DatosRegistro(string? TelefonoPasajero = null, string? NombrePasajero = null,
    string? Destino = null, string? Observaciones = null, double? Latitud = null, double? Longitud = null,
    double? DestinoLatitud = null, double? DestinoLongitud = null)
{
    /// <summary>Par opcional: ambos nulos, o ambos finitos dentro de rango. Nunca se infieren coordenadas del texto.</summary>
    public static bool ParValido(double? lat, double? lng) => lat.HasValue == lng.HasValue &&
        (!lat.HasValue || (double.IsFinite(lat.Value) && lat.Value is >= -90 and <= 90 && double.IsFinite(lng!.Value) && lng.Value is >= -180 and <= 180));
    internal static Point? Punto(double? lat, double? lng) => lat.HasValue ? new Point(lng!.Value, lat.Value) { SRID = 4326 } : null;
}
public sealed class CentralNoAutorizadaException : Exception { }

// La fila del contador permanece bloqueada hasta el COMMIT: el orden refleja confirmaciones.
public sealed class RegistroSolicitudes(RadioMovilDbContext db, RegistroTiempos? tiempos = null, ILogger<RegistroSolicitudes>? logger = null, IDespachoGuard? guard = null)
{
    private readonly RegistroTiempos tiempos = tiempos ?? new RegistroTiempos();
    public async Task<(Guid solicitudId, long orden)> RegistrarAsync(
        Guid empresaId, Guid centralId, Guid actorId, string origen, string claveIdempotencia,
        DateTimeOffset fechaUtc, CancellationToken cancellationToken = default)
    {
        var result = await RegistrarConResultadoAsync(empresaId, centralId, actorId, origen, claveIdempotencia, fechaUtc, cancellationToken);
        return (result.solicitudId, result.orden);
    }

    public async Task<(Guid solicitudId, long orden, bool creada)> RegistrarConResultadoAsync(
        Guid empresaId, Guid centralId, Guid actorId, string origen, string claveIdempotencia,
        DateTimeOffset fechaUtc, CancellationToken cancellationToken = default, DatosRegistro? datos = null)
    {
        datos ??= new DatosRegistro();
        Identidad.Exigir(empresaId, nameof(empresaId));
        Identidad.Exigir(centralId, nameof(centralId));
        origen = Identidad.Texto(origen, nameof(origen));
        if (origen.Length > 500) throw new ArgumentException("Origen excede 500 caracteres.", nameof(origen));
        if (fechaUtc.Offset != TimeSpan.Zero) throw new ArgumentException("La fecha debe estar en UTC.", nameof(fechaUtc));
        if (string.IsNullOrWhiteSpace(claveIdempotencia) || claveIdempotencia.Length > 160) throw new ArgumentException("Clave obligatoria, máximo 160 caracteres.", nameof(claveIdempotencia));
        if (!DatosRegistro.ParValido(datos.Latitud, datos.Longitud) || !DatosRegistro.ParValido(datos.DestinoLatitud, datos.DestinoLongitud))
            throw new ArgumentException("Las coordenadas deben ser un par válido.", nameof(datos));
        if (datos.TelefonoPasajero?.Length > 40 || datos.NombrePasajero?.Length > 160 ||
            datos.Destino?.Length > 500 || datos.Observaciones?.Length > 1000)
            throw new ArgumentException("Datos de solicitud demasiado largos.", nameof(datos));
        var openedHere = db.Database.GetDbConnection().State == ConnectionState.Closed;
        if (openedHere)
        {
            try { await tiempos.MeasureAsync("connection", () => db.Database.OpenConnectionAsync(cancellationToken)); }
            catch (Exception ex)
            {
            logger?.LogWarning("Registro fallido etapa=connection tipo={Type} inner={InnerType}",
                ex.GetType().Name, ex.InnerException?.GetType().Name);
                throw;
            }
        }
        var transactionStart = Stopwatch.GetTimestamp();
        try
        {
        await using var transaction = await tiempos.MeasureAsync("begin", () => db.Database.BeginTransactionAsync(cancellationToken));
        // Protege también el resultado idempotente; FOR SHARE en la membresía
        // impide que una revocación confirme antes de esta operación.
        if (guard is not null) await guard.AuthorizeAsync(empresaId, actorId, "registrar", null, cancellationToken);
        // La clave se reclama antes de consumir un número; reintentos concurrentes se serializan en el índice único.
        var solicitud = new Solicitud(empresaId, origen, fechaUtc, actorId);
        var claimed = await tiempos.MeasureAsync("idempotency_sql", () => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO claves_idempotencia ("EmpresaId", "Ambito", "Clave", "SolicitudId")
            VALUES ({empresaId}, {"registro_solicitud"}, {claveIdempotencia}, {solicitud.Id})
            ON CONFLICT ("EmpresaId", "Ambito", "Clave") DO NOTHING
            """, cancellationToken));
        if (claimed == 0)
        {
            var existing = await tiempos.MeasureAsync("replay_sql", () => db.Idempotencias.AsNoTracking().SingleAsync(x => x.EmpresaId == empresaId && x.Ambito == "registro_solicitud" && x.Clave == claveIdempotencia, cancellationToken));
            var previous = await tiempos.MeasureAsync("replay_row_sql", () => db.Solicitudes.Where(x => x.Id == existing.SolicitudId && x.EmpresaId == empresaId)
                .Select(x => new { x.Origen, CentralId = EF.Property<Guid>(x, "CentralId"), Orden = EF.Property<long>(x, "OrdenRecepcion"),
                    Telefono = EF.Property<string?>(x, "TelefonoPasajero"), Nombre = EF.Property<string?>(x, "NombrePasajero"),
                    Destino = EF.Property<string?>(x, "Destino"), Observaciones = EF.Property<string?>(x, "Observaciones"),
                    Punto = EF.Property<Point?>(x, "PuntoRecogida"), PuntoDestino = EF.Property<Point?>(x, "PuntoDestino") })
                .SingleAsync(cancellationToken));
            if (previous.CentralId != centralId || !StringComparer.Ordinal.Equals(previous.Origen, origen) ||
                previous.Telefono != datos.TelefonoPasajero || previous.Nombre != datos.NombrePasajero ||
                previous.Destino != datos.Destino || previous.Observaciones != datos.Observaciones ||
                previous.Punto?.Y != datos.Latitud || previous.Punto?.X != datos.Longitud ||
                previous.PuntoDestino?.Y != datos.DestinoLatitud || previous.PuntoDestino?.X != datos.DestinoLongitud)
                throw new ConflictoIdempotenciaException();
            await tiempos.MeasureAsync("commit", () => transaction.CommitAsync(cancellationToken));
            return (existing.SolicitudId, previous.Orden, false);
        }
        // Sólo para solicitudes nuevas: un replay confirmado sigue siendo recuperable aunque
        // la central se haya desactivado. La membresía se revalida también en el replay.
        // FOR SHARE impide desactivar la central antes de confirmar el registro nuevo.
        await using (var centralCommand = db.Database.GetDbConnection().CreateCommand())
        {
            centralCommand.Transaction = transaction.GetDbTransaction();
            centralCommand.CommandText = "SELECT 1 FROM centrales WHERE \"EmpresaId\" = @empresa AND \"Id\" = @central AND \"Activa\" FOR SHARE";
            var e = centralCommand.CreateParameter(); e.ParameterName = "empresa"; e.Value = empresaId; centralCommand.Parameters.Add(e);
            var c = centralCommand.CreateParameter(); c.ParameterName = "central"; c.Value = centralId; centralCommand.Parameters.Add(c);
            if (await centralCommand.ExecuteScalarAsync(cancellationToken) is null) throw new CentralNoAutorizadaException();
        }
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = """
            INSERT INTO contadores_central ("EmpresaId", "CentralId", "UltimoOrden")
            VALUES (@empresa, @central, 1)
            ON CONFLICT ("EmpresaId", "CentralId") DO UPDATE
            SET "UltimoOrden" = contadores_central."UltimoOrden" + 1
            RETURNING "UltimoOrden"
            """;
        var empresaParameter = command.CreateParameter(); empresaParameter.ParameterName = "empresa"; empresaParameter.Value = empresaId; command.Parameters.Add(empresaParameter);
        var centralParameter = command.CreateParameter(); centralParameter.ParameterName = "central"; centralParameter.Value = centralId; command.Parameters.Add(centralParameter);
        var order = (long)(await tiempos.MeasureAsync("counter_sql", () => command.ExecuteScalarAsync(cancellationToken)) ?? throw new InvalidOperationException("El contador no devolvió orden."));
        db.Solicitudes.Add(solicitud);
        db.Entry(solicitud).Property("CentralId").CurrentValue = centralId;
        db.Entry(solicitud).Property("OrdenRecepcion").CurrentValue = order;
        db.Entry(solicitud).Property("TelefonoPasajero").CurrentValue = datos.TelefonoPasajero;
        db.Entry(solicitud).Property("NombrePasajero").CurrentValue = datos.NombrePasajero;
        db.Entry(solicitud).Property("Destino").CurrentValue = datos.Destino;
        db.Entry(solicitud).Property("Observaciones").CurrentValue = datos.Observaciones;
        db.Entry(solicitud).Property("PuntoRecogida").CurrentValue = DatosRegistro.Punto(datos.Latitud, datos.Longitud);
        db.Entry(solicitud).Property("PuntoDestino").CurrentValue = DatosRegistro.Punto(datos.DestinoLatitud, datos.DestinoLongitud);
        // Este flujo hace un solo SaveChanges y ante cualquier error abandona TODA la
        // transacción. El savepoint automático añade un viaje de red mientras se
        // retiene el bloqueo del contador; no se usa recuperación parcial aquí.
        var previousSavepoints = db.Database.AutoSavepointsEnabled;
        try
        {
            db.Database.AutoSavepointsEnabled = false;
            await tiempos.MeasureAsync("save_sql", () => db.SaveChangesAsync(cancellationToken));
        }
        finally { db.Database.AutoSavepointsEnabled = previousSavepoints; }
        await tiempos.MeasureAsync("commit", () => transaction.CommitAsync(cancellationToken));
        return (solicitud.Id, order, true);
        }
        catch (Exception ex)
        {
            var pg = ex as Npgsql.PostgresException ?? ex.InnerException as Npgsql.PostgresException;
            logger?.LogWarning("Registro fallido etapa={Stage} tipo={Type} inner={InnerType} sqlState={SqlState}",
                tiempos.EtapaActual, ex.GetType().Name, ex.InnerException?.GetType().Name, pg?.SqlState);
            throw;
        }
        finally
        {
            tiempos.TransactionMs = Stopwatch.GetElapsedTime(transactionStart).TotalMilliseconds;
            if (openedHere) await db.Database.CloseConnectionAsync();
        }
    }
}
