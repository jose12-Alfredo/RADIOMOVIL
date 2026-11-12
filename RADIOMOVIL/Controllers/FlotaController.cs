using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RadioMovil.Domain;
using RadioMovil.Infrastructure;
using RADIOMOVIL.Security;

namespace RADIOMOVIL.Controllers;

// Fase B. Program.MapControllers restringe estas rutas al entorno Testing.
// Administrador: alta de conductores y vehículos. Administrador u Operador: turnos y consultas.
[ApiController]
[Authorize]
[Route("api/v1")]
public sealed class FlotaController(GestionFlota flota, AccessScope scope) : ControllerBase
{
    public sealed record ConductorRequest(string? Nombre);
    public sealed record VehiculoRequest(string? Patente);
    public sealed record AbrirTurnoRequest(Guid ConductorId, Guid VehiculoId);
    public sealed record CerrarTurnoRequest(string? Motivo);
    public sealed record TurnoActivoResponse(Guid Id, Guid VehiculoId, string Patente, DateTimeOffset Inicio);
    public sealed record ConductorResponse(Guid Id, string Nombre, string Estado, string? Ocupacion, TurnoActivoResponse? Turno, string? AccesoEmail);
    public sealed record VehiculoResponse(Guid Id, string Patente, Guid? TurnoId, Guid? ConductorId, string? ConductorNombre);
    public sealed record TurnoResponse(Guid Id, Guid ConductorId, string ConductorNombre, Guid VehiculoId, string Patente,
        DateTimeOffset Inicio, DateTimeOffset? Fin, string? MotivoCierre);

    private Task<bool> Consulta(CancellationToken ct) => scope.HasRoleAsync(ct, RolesEmpresa.Administrador, RolesEmpresa.Operador);
    private Task<bool> Administra(CancellationToken ct) => scope.HasRoleAsync(ct, RolesEmpresa.Administrador);

    [HttpGet("conductores")]
    public async Task<IActionResult> Conductores(CancellationToken ct)
    {
        if (!await Consulta(ct)) return Forbid();
        var rows = await flota.ConductoresAsync(scope.EmpresaId, ct);
        return Ok(rows.Select(x => new ConductorResponse(x.Id, x.Nombre, x.Estado.ToString(), x.Ocupacion,
            x.Turno is { } t ? new TurnoActivoResponse(t.Id, t.VehiculoId, t.Patente, t.Inicio) : null, x.AccesoEmail)));
    }

    [HttpPost("conductores")]
    public async Task<IActionResult> RegistrarConductor([FromBody] ConductorRequest? body, CancellationToken ct)
    {
        if (!await Administra(ct)) return Forbid();
        if (string.IsNullOrWhiteSpace(body?.Nombre) || body.Nombre.Trim().Length > 160) return BadRequest("Nombre obligatorio (máximo 160 caracteres).");
        return await Run(key => flota.RegistrarConductorAsync(scope.EmpresaId, scope.ActorId, body.Nombre, key, ct), created: true);
    }

    [HttpGet("vehiculos")]
    public async Task<IActionResult> Vehiculos(CancellationToken ct)
    {
        if (!await Consulta(ct)) return Forbid();
        var rows = await flota.VehiculosAsync(scope.EmpresaId, ct);
        return Ok(rows.Select(x => new VehiculoResponse(x.Id, x.Patente, x.TurnoId, x.ConductorId, x.ConductorNombre)));
    }

    [HttpPost("vehiculos")]
    public async Task<IActionResult> RegistrarVehiculo([FromBody] VehiculoRequest? body, CancellationToken ct)
    {
        if (!await Administra(ct)) return Forbid();
        if (string.IsNullOrWhiteSpace(body?.Patente)) return BadRequest("Placa obligatoria.");
        return await Run(key => flota.RegistrarVehiculoAsync(scope.EmpresaId, scope.ActorId, body.Patente, key, ct), created: true);
    }

    [HttpGet("turnos")]
    public async Task<IActionResult> Turnos([FromQuery] bool? abiertos, [FromQuery] Guid? conductorId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        if (!await Consulta(ct)) return Forbid();
        if (page < 1 || page > 1_000_000 || pageSize is < 1 or > 100 || conductorId == Guid.Empty) return BadRequest("Paginación o conductor inválido.");
        var (total, items) = await flota.TurnosAsync(scope.EmpresaId, abiertos, conductorId, page, pageSize, ct);
        return Ok(new { page, pageSize, total, items = items.Select(x => new TurnoResponse(x.Id, x.ConductorId, x.ConductorNombre,
            x.VehiculoId, x.Patente, x.Inicio, x.Fin, x.MotivoCierre)) });
    }

    [HttpPost("turnos")]
    public async Task<IActionResult> AbrirTurno([FromBody] AbrirTurnoRequest? body, CancellationToken ct)
    {
        if (!await Consulta(ct)) return Forbid();
        if (body is null || body.ConductorId == Guid.Empty || body.VehiculoId == Guid.Empty) return BadRequest("Conductor y vehículo obligatorios.");
        return await Run(key => flota.AbrirTurnoAsync(scope.EmpresaId, scope.ActorId, body.ConductorId, body.VehiculoId, key, ct), created: true);
    }

    [HttpPost("turnos/{turnoId:guid}/cerrar")]
    public async Task<IActionResult> CerrarTurno(Guid turnoId, [FromBody] CerrarTurnoRequest? body, CancellationToken ct)
    {
        if (!await Consulta(ct)) return Forbid();
        if (body?.Motivo is { Length: > 500 }) return BadRequest("El motivo admite hasta 500 caracteres.");
        return await Run(key => flota.CerrarTurnoAsync(scope.EmpresaId, scope.ActorId, turnoId, body?.Motivo, key, ct));
    }

    // Fase C: vehículos con los que cada conductor puede iniciar turno por sí mismo.
    public sealed record AutorizarRequest(Guid ConductorId, Guid VehiculoId);
    public sealed record AutorizacionResponse(Guid Id, Guid ConductorId, string ConductorNombre, Guid VehiculoId, string Patente, bool VehiculoLibre, DateTimeOffset AutorizadaEn);

    [HttpGet("autorizaciones-vehiculo")]
    public async Task<IActionResult> Autorizaciones([FromQuery] Guid? conductorId, CancellationToken ct)
    {
        if (!await Consulta(ct)) return Forbid();
        if (conductorId == Guid.Empty) return BadRequest("Conductor inválido.");
        var rows = await flota.AutorizacionesAsync(scope.EmpresaId, conductorId, ct);
        return Ok(rows.Select(x => new AutorizacionResponse(x.Id, x.ConductorId, x.ConductorNombre, x.VehiculoId, x.Patente, x.VehiculoLibre, x.AutorizadaEn)));
    }

    [HttpPost("autorizaciones-vehiculo")]
    public async Task<IActionResult> Autorizar([FromBody] AutorizarRequest? body, CancellationToken ct)
    {
        if (!await Administra(ct)) return Forbid();
        if (body is null || body.ConductorId == Guid.Empty || body.VehiculoId == Guid.Empty) return BadRequest("Conductor y vehículo obligatorios.");
        return await Run(key => flota.AutorizarVehiculoAsync(scope.EmpresaId, scope.ActorId, body.ConductorId, body.VehiculoId, key, ct), created: true);
    }

    [HttpPost("autorizaciones-vehiculo/{autorizacionId:guid}/revocar")]
    public async Task<IActionResult> Revocar(Guid autorizacionId, CancellationToken ct)
    {
        if (!await Administra(ct)) return Forbid();
        return await Run(key => flota.RevocarAutorizacionAsync(scope.EmpresaId, scope.ActorId, autorizacionId, key, ct));
    }

    public sealed record UbicacionResponse(Guid ConductorId, string Nombre, Guid TurnoId, string Patente, double? Latitud, double? Longitud,
        double? PrecisionMetros, DateTimeOffset? CapturadaEn, DateTimeOffset? RecibidaEn, int? AntiguedadSegundos, string EstadoGps);

    /// <summary>Última ubicación REAL por conductor en turno, clasificada con el reloj del servidor.</summary>
    [HttpGet("ubicaciones")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Ubicaciones([FromServices] RecepcionGps gps, [FromServices] GpsOptions opciones, CancellationToken ct)
    {
        if (!await Consulta(ct)) return Forbid();
        var (ahora, items) = await gps.UltimasAsync(scope.EmpresaId, ct);
        return Ok(new
        {
            servidorAhora = ahora,
            umbrales = new { recienteSegundos = opciones.RecienteSegundos, desactualizadoSegundos = opciones.DesactualizadoSegundos },
            items = items.Select(x => new UbicacionResponse(x.ConductorId, x.Nombre, x.TurnoId, x.Patente, x.Latitud, x.Longitud,
                x.PrecisionMetros, x.CapturadaEn, x.RecibidaEn, x.AntiguedadSegundos, x.Estado.ToString())),
        });
    }

    private Task<IActionResult> Run(Func<string, Task<ResultadoFlota>> operation, bool created = false) =>
        FlotaHttp.RunAsync(this, operation, created);
}
