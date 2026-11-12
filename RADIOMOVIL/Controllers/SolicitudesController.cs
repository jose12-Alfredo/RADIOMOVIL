using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using RadioMovil.Domain;
using RadioMovil.Infrastructure;
using RADIOMOVIL.Security;
using System.Globalization;
using NetTopologySuite.Geometries;

namespace RADIOMOVIL.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/solicitudes")]
public sealed class SolicitudesController(RadioMovilDbContext db, RegistroSolicitudes registro, AccessScope scope, RegistroTiempos tiempos) : ControllerBase
{
    public sealed record CrearSolicitudRequest(Guid CentralId, string? Origen, string? TelefonoPasajero = null,
        string? NombrePasajero = null, string? Destino = null, string? Observaciones = null,
        double? Latitud = null, double? Longitud = null, double? DestinoLatitud = null, double? DestinoLongitud = null);
    public sealed record SolicitudResponse(Guid Id, Guid EmpresaId, Guid CentralId, string Origen,
        EstadoSolicitud Estado, DateTimeOffset CreadaEn, long OrdenRecepcion,
        string? TelefonoPasajero, string? NombrePasajero, string? Destino, string? Observaciones,
        double? Latitud, double? Longitud, double? DestinoLatitud = null, double? DestinoLongitud = null);

    private Task<bool> Autorizado(CancellationToken ct) => scope.HasRoleAsync(ct, RolesEmpresa.Operador, RolesEmpresa.Administrador);
    private IQueryable<Solicitud> TenantQuery() => db.Solicitudes.AsNoTracking().Where(x => x.EmpresaId == scope.EmpresaId);

    private async Task<SolicitudResponse?> FindAsync(Guid id, CancellationToken ct)
    {
        var row = await tiempos.MeasureAsync("read_sql", () => TenantQuery().Where(x => x.Id == id).Select(x => new
        {
            x.Id, x.EmpresaId, x.Origen, x.Estado, x.CreadaEn,
            CentralId = EF.Property<Guid>(x, "CentralId"), Orden = EF.Property<long>(x, "OrdenRecepcion"),
            Telefono = EF.Property<string?>(x, "TelefonoPasajero"), Nombre = EF.Property<string?>(x, "NombrePasajero"),
            Destino = EF.Property<string?>(x, "Destino"), Observaciones = EF.Property<string?>(x, "Observaciones"),
            Punto = EF.Property<Point?>(x, "PuntoRecogida"), PuntoDestino = EF.Property<Point?>(x, "PuntoDestino")
        }).SingleOrDefaultAsync(ct));
        return row is null ? null : new(row.Id, row.EmpresaId, row.CentralId, row.Origen, row.Estado, row.CreadaEn, row.Orden,
            row.Telefono, row.Nombre, row.Destino, row.Observaciones, row.Punto?.Y, row.Punto?.X, row.PuntoDestino?.Y, row.PuntoDestino?.X);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CrearSolicitudRequest? request, CancellationToken ct)
    {
        if (!await Autorizado(ct)) return Forbid();
        Response.OnStarting(() =>
        {
            Response.Headers["Server-Timing"] = string.Join(",", tiempos.Duraciones.Select(x =>
                $"{x.Key};dur={x.Value.ToString("F2", CultureInfo.InvariantCulture)}")) +
                $",transaction;dur={tiempos.TransactionMs.ToString("F2", CultureInfo.InvariantCulture)}";
            return Task.CompletedTask;
        });
        if (request is null || request.CentralId == Guid.Empty || string.IsNullOrWhiteSpace(request.Origen) || request.Origen.Length > 500)
            return BadRequest("CentralId y Origen (máximo 500 caracteres) son obligatorios.");
        if (request.TelefonoPasajero?.Length > 40 || request.NombrePasajero?.Length > 160 ||
            request.Destino?.Length > 500 || request.Observaciones?.Length > 1000 ||
            !DatosRegistro.ParValido(request.Latitud, request.Longitud) ||
            !DatosRegistro.ParValido(request.DestinoLatitud, request.DestinoLongitud))
            return BadRequest("Datos opcionales o coordenadas inválidos.");
        if (!Request.Headers.TryGetValue("Idempotency-Key", out var header) || header.Count != 1 ||
            string.IsNullOrWhiteSpace(header[0]) || header[0]!.Length > 160)
            return BadRequest("Idempotency-Key es obligatorio (máximo 160 caracteres).");
        try
        {
            var fechaUtc = DateTimeOffset.UtcNow;
            var result = await registro.RegistrarConResultadoAsync(scope.EmpresaId, request.CentralId, scope.ActorId,
                request.Origen, header[0]!, fechaUtc, ct, new DatosRegistro(request.TelefonoPasajero, request.NombrePasajero,
                    request.Destino, request.Observaciones, request.Latitud, request.Longitud, request.DestinoLatitud, request.DestinoLongitud));
            // El COMMIT ya confirmó la escritura; la respuesta nueva usa esos mismos datos.
            // Evita una lectura adicional que antes podía fallar DESPUÉS del COMMIT.
            if (result.creada)
            {
                var created = new SolicitudResponse(result.solicitudId, scope.EmpresaId, request.CentralId,
                    request.Origen.Trim(), EstadoSolicitud.Pendiente, fechaUtc, result.orden,
                    request.TelefonoPasajero, request.NombrePasajero, request.Destino, request.Observaciones, request.Latitud, request.Longitud,
                    request.DestinoLatitud, request.DestinoLongitud);
                return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
            }
            var response = await FindAsync(result.solicitudId, ct);
            if (response is null) throw new InvalidOperationException("La solicitud confirmada no pudo recuperarse.");
            return Ok(response);
        }
        catch (ConflictoIdempotenciaException) { return Conflict("Idempotency-Key ya fue utilizada con otra central u origen."); }
        catch (CentralNoAutorizadaException) { return BadRequest("La central no está activa o no pertenece a tu empresa."); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (ErrorDominio) { return BadRequest("Datos de solicitud inválidos."); }
        // Fallos de PostgreSQL no se convierten en HTTP 201: el middleware devuelve 500.
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        if (!await Autorizado(ct)) return Forbid();
        var result = await FindAsync(id, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? centralId, [FromQuery] string? estado,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        if (!await Autorizado(ct)) return Forbid();
        if (page < 1 || page > 1_000_000 || pageSize is < 1 or > 100 || centralId == Guid.Empty)
            return BadRequest("Paginación o central inválida.");
        EstadoSolicitud? filter = null;
        if (estado is not null)
        {
            if (!Enum.TryParse<EstadoSolicitud>(estado, true, out var parsed) || !Enum.IsDefined(parsed))
                return BadRequest("Estado inválido.");
            filter = parsed;
        }
        var query = TenantQuery();
        if (centralId.HasValue) query = query.Where(x => EF.Property<Guid>(x, "CentralId") == centralId.Value);
        if (filter.HasValue) query = query.Where(x => x.Estado == filter.Value);
        var total = await query.CountAsync(ct);
        var ordered = centralId.HasValue
            ? query.OrderBy(x => EF.Property<long>(x, "OrdenRecepcion")).ThenBy(x => x.Id)
            : query.OrderBy(x => x.CreadaEn).ThenBy(x => x.Id);
        var rows = await ordered.Skip((page - 1) * pageSize).Take(pageSize).Select(x => new
        {
            x.Id, x.EmpresaId, x.Origen, x.Estado, x.CreadaEn,
            CentralId = EF.Property<Guid>(x, "CentralId"), Orden = EF.Property<long>(x, "OrdenRecepcion"),
            Telefono = EF.Property<string?>(x, "TelefonoPasajero"), Nombre = EF.Property<string?>(x, "NombrePasajero"),
            Destino = EF.Property<string?>(x, "Destino"), Observaciones = EF.Property<string?>(x, "Observaciones"),
            Punto = EF.Property<Point?>(x, "PuntoRecogida"), PuntoDestino = EF.Property<Point?>(x, "PuntoDestino")
        }).ToListAsync(ct);
        return Ok(new { page, pageSize, total, items = rows.Select(x => new SolicitudResponse(x.Id, x.EmpresaId, x.CentralId, x.Origen, x.Estado, x.CreadaEn, x.Orden,
            x.Telefono, x.Nombre, x.Destino, x.Observaciones, x.Punto?.Y, x.Punto?.X, x.PuntoDestino?.Y, x.PuntoDestino?.X)) });
    }
}
