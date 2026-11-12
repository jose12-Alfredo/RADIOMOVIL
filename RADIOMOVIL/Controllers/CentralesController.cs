using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RadioMovil.Infrastructure;
using RADIOMOVIL.Security;

namespace RADIOMOVIL.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/centrales")]
public sealed class CentralesController(RadioMovilDbContext db, AccessScope scope) : ControllerBase
{
    public sealed record CrearCentralRequest(string? Nombre);
    public sealed record CentralResponse(Guid Id, string Nombre);

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        if (!await scope.HasRoleAsync(ct, RolesEmpresa.Administrador, RolesEmpresa.Operador)) return Forbid();
        var centrales = await db.Centrales.AsNoTracking()
            .Where(c => c.EmpresaId == scope.EmpresaId && c.Activa)
            .OrderBy(c => c.Nombre).ThenBy(c => c.Id)
            .Select(c => new CentralResponse(c.Id, c.Nombre)).ToListAsync(ct);
        return Ok(centrales);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CrearCentralRequest? body, CancellationToken ct)
    {
        if (!await scope.HasRoleAsync(ct, RolesEmpresa.Administrador)) return Forbid();
        var name = body?.Nombre?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 160) return BadRequest("Nombre obligatorio (máximo 160 caracteres).");
        var central = new Central { Id = Guid.NewGuid(), EmpresaId = scope.EmpresaId, Nombre = name };
        db.Centrales.Add(central);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { return Conflict("Ya existe una central con ese nombre en la empresa."); }
        return StatusCode(StatusCodes.Status201Created, new CentralResponse(central.Id, central.Nombre));
    }
}
