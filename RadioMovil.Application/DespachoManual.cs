using RadioMovil.Domain;

namespace RadioMovil.Application;

public sealed record CandidatoDespacho(Guid ConductorId, string Nombre, bool UbicacionVerificada);
public sealed record ResultadoDespacho(Guid SolicitudId, Guid? IntentoId, Guid? ViajeId,
    EstadoSolicitud Estado, DateTimeOffset Fecha, bool Repetida);

public interface IDespachoManual
{
    Task<IReadOnlyList<CandidatoDespacho>> CandidatosAsync(Guid empresaId, CancellationToken ct = default);
    /// <summary>Asignación directa por Administrador/Operador: crea el viaje sin oferta ni aceptación del conductor.</summary>
    Task<ResultadoDespacho> AsignarAsync(Guid empresaId, Guid solicitudId, Guid conductorId, Guid actorId, string clave, CancellationToken ct = default);
    Task<ResultadoDespacho> OfrecerAsync(Guid empresaId, Guid solicitudId, Guid conductorId, Guid actorId, string clave, CancellationToken ct = default);
    Task<ResultadoDespacho> AceptarAsync(Guid empresaId, Guid solicitudId, Guid intentoId, Guid actorId, string clave, CancellationToken ct = default);
    Task<ResultadoDespacho> RechazarAsync(Guid empresaId, Guid solicitudId, Guid intentoId, Guid actorId, string motivo, string clave, CancellationToken ct = default);
    Task<ResultadoDespacho> VencerAsync(Guid empresaId, Guid solicitudId, Guid intentoId, Guid actorId, string clave, CancellationToken ct = default);
    Task<ResultadoDespacho> CancelarClienteAsync(Guid empresaId, Guid solicitudId, Guid actorId, string motivo, string clave, CancellationToken ct = default);
    Task<ResultadoDespacho> CancelarConductorAsync(Guid empresaId, Guid solicitudId, Guid viajeId, Guid actorId, string motivo, string clave, CancellationToken ct = default);
}
