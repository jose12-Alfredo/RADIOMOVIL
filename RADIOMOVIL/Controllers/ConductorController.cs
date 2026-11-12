using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RadioMovil.Domain;
using RadioMovil.Infrastructure;
using RADIOMOVIL.Rutas;
using RADIOMOVIL.Security;

namespace RADIOMOVIL.Controllers;

// Fase C: contratos del CONDUCTOR (app Flutter con Bearer, o emisor web de prueba con cookie+CSRF).
// El conductor se deduce SIEMPRE de la membresía de la sesión; nunca del cuerpo ni de la ruta.
[ApiController]
[Authorize]
[Route("api/v1/conductor")]
public sealed class ConductorController(GestionFlota flota, RecepcionGps gps, Carreras carreras, PasosViaje pasos, ServicioRutas rutas, AccessScope scope) : ControllerBase
{
    public sealed record AbrirTurnoRequest(Guid VehiculoId);
    public sealed record CerrarTurnoRequest(string? Motivo);
    public sealed record UbicacionRequest(double? Latitud, double? Longitud, double? PrecisionMetros, DateTimeOffset? CapturadaEn);
    public sealed record TurnoResponse(Guid Id, Guid VehiculoId, string Patente, DateTimeOffset Inicio);
    public sealed record YoResponse(Guid ConductorId, string Nombre, string Estado, string? Ocupacion, TurnoResponse? Turno,
        string EstadoGps, DateTimeOffset? UltimaRecepcion, int IntervaloEnvioSegundos, Guid? CarreraActivaId);
    public sealed record RecepcionResponse(Guid ViajeId, DateTimeOffset RecibidaEn, bool Repetida);
    public sealed record VehiculoAutorizadoResponse(Guid VehiculoId, string Patente, bool Libre);
    public sealed record FinalizarRequest(string? Observaciones);
    public sealed record PasoResponse(Guid ViajeId, string Estado, DateTimeOffset En, bool Repetida);
    public sealed record GeometriaRuta(string Type, IReadOnlyList<double[]> Coordinates);
    public sealed record RutaResponse(string HaciaPunto, double DistanciaMetros, double DuracionSegundos, GeometriaRuta Geometria, DateTimeOffset CalculadaEn);
    public sealed record UbicacionResponse(bool Aceptada, string? Motivo, DateTimeOffset RecibidaEn, string EstadoGps, int IntervaloMinimoSegundos);

    private async Task<Guid?> ConductorPropioAsync(CancellationToken ct) =>
        await scope.MembershipAsync(ct) is { Rol: RolesEmpresa.Conductor, ConductorId: { } id } ? id : null;

    [HttpGet("yo")]
    public async Task<IActionResult> Yo(CancellationToken ct)
    {
        if (await ConductorPropioAsync(ct) is not { } conductorId) return Forbid();
        var yo = await gps.PropioAsync(scope.EmpresaId, conductorId, flota, ct);
        if (yo is null) return NotFound();
        return Ok(new YoResponse(yo.ConductorId, yo.Nombre, yo.Estado.ToString(), yo.Ocupacion,
            yo.Turno is { } t ? new TurnoResponse(t.Id, t.VehiculoId, t.Patente, t.Inicio) : null,
            yo.EstadoGps.ToString(), yo.UltimaRecepcion, yo.IntervaloEnvioSegundos, yo.CarreraActivaId));
    }

    /// <summary>Carreras asignadas activas del conductor (Asignado/EnCurso). La app las recupera al abrir y al reconectar.</summary>
    [HttpGet("carreras")]
    public async Task<IActionResult> MisCarreras(CancellationToken ct)
    {
        if (await ConductorPropioAsync(ct) is not { } conductorId) return Forbid();
        return Ok(await carreras.DelConductorAsync(scope.EmpresaId, conductorId, ct));
    }

    /// <summary>Confirma que la app MOSTRÓ la orden. Entrega, no aceptación: no cambia el estado. Idempotente sin clave.</summary>
    [HttpPost("carreras/{viajeId:guid}/recibida")]
    public async Task<IActionResult> Recibida(Guid viajeId, CancellationToken ct)
    {
        if (await ConductorPropioAsync(ct) is not { } conductorId) return Forbid();
        try
        {
            var (recibidaEn, repetida) = await carreras.ConfirmarRecepcionAsync(scope.EmpresaId, scope.ActorId, conductorId, viajeId, ct);
            return Ok(new RecepcionResponse(viajeId, recibidaEn, repetida));
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (CarreraNoEncontradaException) { return NotFound(); }
        catch (ErrorDominio e) { return Conflict(e.Message); }
    }

    // Pasos del conductor (2026-11-10). Solo el dueño, con turno abierto e Idempotency-Key. 404 ajena/inexistente; 409 paso inválido o cerrada.
    [HttpPost("carreras/{viajeId:guid}/en-camino")]
    public Task<IActionResult> EnCamino(Guid viajeId, CancellationToken ct) => Paso(viajeId, PasoViaje.EnCamino, null, ct);

    [HttpPost("carreras/{viajeId:guid}/llegada")]
    public Task<IActionResult> Llegada(Guid viajeId, CancellationToken ct) => Paso(viajeId, PasoViaje.Llegada, null, ct);

    [HttpPost("carreras/{viajeId:guid}/iniciar")]
    public Task<IActionResult> Iniciar(Guid viajeId, CancellationToken ct) => Paso(viajeId, PasoViaje.Iniciar, null, ct);

    [HttpPost("carreras/{viajeId:guid}/finalizar")]
    public Task<IActionResult> Finalizar(Guid viajeId, [FromBody] FinalizarRequest? body, CancellationToken ct)
    {
        if (body?.Observaciones is { Length: > 500 }) return Task.FromResult<IActionResult>(BadRequest("Las observaciones admiten hasta 500 caracteres."));
        return Paso(viajeId, PasoViaje.Finalizar, body?.Observaciones, ct);
    }

    private async Task<IActionResult> Paso(Guid viajeId, PasoViaje paso, string? observaciones, CancellationToken ct)
    {
        if (await ConductorPropioAsync(ct) is not { } conductorId) return Forbid();
        if (FlotaHttp.Clave(Request) is not { } clave) return BadRequest("Idempotency-Key obligatoria (máximo 160 caracteres).");
        try
        {
            var r = await pasos.AvanzarAsync(scope.EmpresaId, scope.ActorId, conductorId, viajeId, paso, observaciones, clave, ct);
            return Ok(new PasoResponse(r.ViajeId, r.Estado, r.En, r.Repetida));
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (CarreraNoEncontradaException) { return NotFound("Carrera no encontrada."); }
        catch (CarreraConflictoException e) { return Conflict(e.Message); }
        catch (ErrorDominio e) { return Conflict(e.Message); }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.CheckViolation })
        { return Conflict("El paso ya se registró o la carrera cambió. Actualiza la carrera."); }
    }

    /// <summary>Ruta por calles desde la posición actual del conductor hacia la recogida (antes de subir) o el destino (en curso).</summary>
    [HttpGet("carreras/{viajeId:guid}/ruta")]
    public async Task<IActionResult> Ruta(Guid viajeId, [FromQuery] double? desdeLatitud, [FromQuery] double? desdeLongitud, CancellationToken ct)
    {
        if (await ConductorPropioAsync(ct) is not { } conductorId) return Forbid();
        if (desdeLatitud is not { } lat || desdeLongitud is not { } lng || !double.IsFinite(lat) || !double.IsFinite(lng) ||
            lat is < -90 or > 90 || lng is < -180 or > 180)
            return BadRequest("desdeLatitud y desdeLongitud son obligatorias y deben estar en rango.");
        var destino = await pasos.DestinoRutaAsync(scope.EmpresaId, conductorId, viajeId, ct);
        if (destino is null) return NotFound("Carrera no encontrada o cerrada.");
        if (destino is not { Latitud: { } toLat, Longitud: { } toLng })
            return NotFound(destino.HaciaPunto == "Destino" ? "La carrera no tiene destino marcado en el mapa." : "La carrera no tiene recogida marcada en el mapa.");
        try
        {
            var ruta = await rutas.CalcularAsync(lat, lng, toLat, toLng, ct);
            Response.Headers.CacheControl = "no-store";
            return Ok(new RutaResponse(destino.HaciaPunto, ruta.DistanciaMetros, ruta.DuracionSegundos,
                new GeometriaRuta("LineString", ruta.Coordenadas), DateTimeOffset.UtcNow));
        }
        catch (RutaNoDisponibleException e) { return StatusCode(StatusCodes.Status503ServiceUnavailable, e.Message); }
    }

    [HttpGet("vehiculos")]
    public async Task<IActionResult> Vehiculos(CancellationToken ct)
    {
        if (await ConductorPropioAsync(ct) is not { } conductorId) return Forbid();
        var rows = await flota.AutorizacionesAsync(scope.EmpresaId, conductorId, ct);
        return Ok(rows.Select(x => new VehiculoAutorizadoResponse(x.VehiculoId, x.Patente, x.VehiculoLibre)));
    }

    [HttpPost("turno")]
    public async Task<IActionResult> AbrirTurno([FromBody] AbrirTurnoRequest? body, CancellationToken ct)
    {
        if (await ConductorPropioAsync(ct) is not { } conductorId) return Forbid();
        if (body is null || body.VehiculoId == Guid.Empty) return BadRequest("Vehículo obligatorio.");
        return await FlotaHttp.RunAsync(this, key => flota.AbrirTurnoPropioAsync(scope.EmpresaId, scope.ActorId, conductorId, body.VehiculoId, key, ct), created: true);
    }

    [HttpPost("turno/cerrar")]
    public async Task<IActionResult> CerrarTurno([FromBody] CerrarTurnoRequest? body, CancellationToken ct)
    {
        if (await ConductorPropioAsync(ct) is not { } conductorId) return Forbid();
        if (body?.Motivo is { Length: > 500 }) return BadRequest("El motivo admite hasta 500 caracteres.");
        return await FlotaHttp.RunAsync(this, key => flota.CerrarTurnoPropioAsync(scope.EmpresaId, scope.ActorId, conductorId, body?.Motivo, key, ct));
    }

    /// <summary>Coordenadas reales del dispositivo. Sin Idempotency-Key: una captura repetida no cambia nada (misma CapturadaEn).</summary>
    [HttpPost("ubicaciones")]
    public async Task<IActionResult> Ubicacion([FromBody] UbicacionRequest? body, CancellationToken ct)
    {
        if (await ConductorPropioAsync(ct) is not { } conductorId) return Forbid();
        if (body is not { Latitud: { } lat, Longitud: { } lng, PrecisionMetros: { } precision, CapturadaEn: { } capturada })
            return BadRequest("latitud, longitud, precisionMetros y capturadaEn son obligatorios.");
        try
        {
            var result = await gps.RegistrarAsync(scope.EmpresaId, scope.ActorId, conductorId, lat, lng, precision, capturada, ct);
            return Ok(new UbicacionResponse(result.Aceptada, result.Motivo, result.RecibidaEn, result.Estado.ToString(), result.IntervaloMinimoSegundos));
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (GpsSinTurnoException e) { return Conflict(e.Message); }
        catch (GpsFrecuenciaException e)
        {
            Response.Headers.RetryAfter = e.ReintentarEnSegundos.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return StatusCode(StatusCodes.Status429TooManyRequests, "Envío demasiado frecuente.");
        }
        catch (ErrorDominio e) { return BadRequest(e.Message); }
    }
}

/// <summary>Traducción común de operaciones de flota idempotentes a HTTP.</summary>
public static class FlotaHttp
{
    public sealed record OperacionResponse(Guid Id, bool Repetida);

    public static string? Clave(HttpRequest request) => request.Headers.TryGetValue("Idempotency-Key", out var values) && values.Count == 1 &&
        !string.IsNullOrWhiteSpace(values[0]) && values[0]!.Length <= 160 ? values[0] : null;

    public static async Task<IActionResult> RunAsync(ControllerBase controller, Func<string, Task<ResultadoFlota>> operation, bool created = false)
    {
        var clave = Clave(controller.Request);
        if (clave is null) return controller.BadRequest("Idempotency-Key obligatoria (máximo 160 caracteres).");
        try
        {
            var result = await operation(clave);
            var response = new OperacionResponse(result.Id, result.Repetida);
            return created && !result.Repetida ? controller.StatusCode(StatusCodes.Status201Created, response) : controller.Ok(response);
        }
        catch (UnauthorizedAccessException) { return controller.Forbid(); }
        catch (FlotaNoEncontradaException e) { return controller.NotFound(e.Message); }
        catch (FlotaConflictoException e) { return controller.Conflict(e.Message); }
        catch (ErrorDominio e) { return controller.BadRequest(e.Message); }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { return controller.Conflict("Registro duplicado o turno abierto incompatible."); }
        // Otros fallos de PostgreSQL se propagan como 500: nunca se informa éxito sin COMMIT.
    }
}
