using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RadioMovil.Application;
using RadioMovil.Domain;
using RadioMovil.Infrastructure;
using RADIOMOVIL.Security;

namespace RADIOMOVIL.Controllers;

// Program.MapControllers restringe TODAS estas rutas al entorno Testing.
[ApiController]
[Authorize]
[Route("api/v1/despacho")]
public sealed class DespachoController(IDespachoManual despacho, Carreras carreras, AccessScope scope) : ControllerBase
{
    public sealed record OfertaRequest(Guid ConductorId);
    public sealed record AsignarRequest(Guid ConductorId);
    public sealed record MotivoRequest(string? Motivo);

    private Task<bool> PuedeDespachar(CancellationToken ct) => scope.HasRoleAsync(ct, RolesEmpresa.Operador, RolesEmpresa.Administrador);

    [HttpGet("candidatos")]
    public async Task<IActionResult> Candidatos(CancellationToken ct) =>
        !await PuedeDespachar(ct) ? Forbid() : Ok(await despacho.CandidatosAsync(scope.EmpresaId, ct));

    private string? Clave() => Request.Headers.TryGetValue("Idempotency-Key", out var values) && values.Count == 1 &&
        !string.IsNullOrWhiteSpace(values[0]) && values[0]!.Length <= 160 ? values[0] : null;

    private async Task<IActionResult> Run(Func<string, Task<ResultadoDespacho>> operation, bool created = false)
    {
        var clave = Clave();
        if (clave is null) return BadRequest("Idempotency-Key obligatoria (máximo 160 caracteres).");
        try
        {
            var result = await operation(clave);
            return created && !result.Repetida ? StatusCode(StatusCodes.Status201Created, result) : Ok(result);
        }
        catch (SolicitudAjenaONoEncontradaException) { return NotFound(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (DespachoConflictoException e) { return Conflict(e.Message); }
        catch (ErrorDominio) { return Conflict("Transición de estado o datos incompatibles."); }
        catch (DbUpdateConcurrencyException) { return Conflict("La solicitud cambió durante la operación."); }
        catch (PostgresException e) when (e.SqlState is PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.CheckViolation)
        { return Conflict("Reserva incompatible."); }
        catch (DbUpdateException e) when (e.InnerException is PostgresException pg && pg.SqlState is PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.CheckViolation)
        { return Conflict("Reserva incompatible."); }
        // Errores de DB/commit se propagan como 500, nunca como éxito.
    }

    /// <summary>Conductores disponibles para esta solicitud, los más cercanos primero (GPS reciente del turno actual).</summary>
    [HttpGet("solicitudes/{solicitudId:guid}/candidatos")]
    public async Task<IActionResult> CandidatosCercanos(Guid solicitudId, CancellationToken ct)
    {
        if (!await PuedeDespachar(ct)) return Forbid();
        var result = await carreras.CandidatosCercanosAsync(scope.EmpresaId, solicitudId, ct);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>
    /// Asignación DIRECTA (decisión 2026-11-10): la central asigna y el conductor recibe la orden sin aceptarla.
    /// 201 asignada; 200 repetición con la misma clave; 409 conductor ocupado/sin turno o solicitud no pendiente.
    /// </summary>
    [HttpPost("solicitudes/{solicitudId:guid}/asignar")]
    public async Task<IActionResult> Asignar(Guid solicitudId, [FromBody] AsignarRequest? body, CancellationToken ct)
    {
        if (!await PuedeDespachar(ct)) return Forbid();
        if (body is null || body.ConductorId == Guid.Empty) return BadRequest("ConductorId obligatorio.");
        return await Run(key => despacho.AsignarAsync(scope.EmpresaId, solicitudId, body.ConductorId, scope.ActorId, key, ct), created: true);
    }

    [HttpGet("carreras")]
    public async Task<IActionResult> CarrerasActivas(CancellationToken ct) =>
        !await PuedeDespachar(ct) ? Forbid() : Ok(await carreras.ActivasAsync(scope.EmpresaId, ct));

    [HttpPost("solicitudes/{solicitudId:guid}/ofertas")]
    public async Task<IActionResult> Ofrecer(Guid solicitudId, [FromBody] OfertaRequest? body, CancellationToken ct)
    {
        if (!await PuedeDespachar(ct)) return Forbid();
        if (body is null || body.ConductorId == Guid.Empty) return BadRequest("ConductorId obligatorio.");
        return await Run(key => despacho.OfrecerAsync(scope.EmpresaId, solicitudId, body.ConductorId, scope.ActorId, key, ct), created: true);
    }

    [HttpPost("solicitudes/{solicitudId:guid}/ofertas/{intentoId:guid}/aceptar")]
    public Task<IActionResult> Aceptar(Guid solicitudId, Guid intentoId, CancellationToken ct) =>
        Run(key => despacho.AceptarAsync(scope.EmpresaId, solicitudId, intentoId, scope.ActorId, key, ct));

    [HttpPost("solicitudes/{solicitudId:guid}/ofertas/{intentoId:guid}/rechazar")]
    public async Task<IActionResult> Rechazar(Guid solicitudId, Guid intentoId, [FromBody] MotivoRequest? body, CancellationToken ct)
    {
        if (!await scope.HasRoleAsync(ct, RolesEmpresa.Conductor)) return Forbid();
        if (string.IsNullOrWhiteSpace(body?.Motivo)) return BadRequest("Motivo obligatorio.");
        return await Run(key => despacho.RechazarAsync(scope.EmpresaId, solicitudId, intentoId, scope.ActorId, body.Motivo!, key, ct));
    }

    [HttpPost("solicitudes/{solicitudId:guid}/ofertas/{intentoId:guid}/vencer")]
    public Task<IActionResult> Vencer(Guid solicitudId, Guid intentoId, CancellationToken ct) =>
        Run(key => despacho.VencerAsync(scope.EmpresaId, solicitudId, intentoId, scope.ActorId, key, ct));

    [HttpPost("solicitudes/{solicitudId:guid}/cancelar")]
    public async Task<IActionResult> CancelarCliente(Guid solicitudId, [FromBody] MotivoRequest? body, CancellationToken ct)
    {
        if (!await PuedeDespachar(ct)) return Forbid();
        if (string.IsNullOrWhiteSpace(body?.Motivo)) return BadRequest("Motivo obligatorio.");
        return await Run(key => despacho.CancelarClienteAsync(scope.EmpresaId, solicitudId, scope.ActorId, body.Motivo!, key, ct));
    }

    [HttpPost("solicitudes/{solicitudId:guid}/viajes/{viajeId:guid}/cancelar-conductor")]
    public async Task<IActionResult> CancelarConductor(Guid solicitudId, Guid viajeId, [FromBody] MotivoRequest? body, CancellationToken ct)
    {
        if (!await scope.HasRoleAsync(ct, RolesEmpresa.Conductor)) return Forbid();
        if (string.IsNullOrWhiteSpace(body?.Motivo)) return BadRequest("Motivo obligatorio.");
        return await Run(key => despacho.CancelarConductorAsync(scope.EmpresaId, solicitudId, viajeId, scope.ActorId, body.Motivo!, key, ct));
    }
}
