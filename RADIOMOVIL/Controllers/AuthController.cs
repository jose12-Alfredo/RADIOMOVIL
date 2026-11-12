using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RadioMovil.Infrastructure;
using RADIOMOVIL.Security;

namespace RADIOMOVIL.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(RadioMovilDbContext db, UserManager<AppUser> users,
    SignInManager<AppUser> signIn, IAntiforgery antiforgery, IOptionsMonitor<BearerTokenOptions> bearer,
    AccessScope access, IDespachoGuard guard) : ControllerBase
{
    public sealed record LoginRequest(string? Email, string? Password, Guid EmpresaId, string? Cliente);
    public sealed record RefreshRequest(string? RefreshToken);
    public sealed record CreateUserRequest(string? Email, string? Password, string? Rol, Guid? ConductorId);
    public sealed record ChangeUserRequest(string? Rol, Guid? ConductorId, bool Activo);
    public sealed record SessionResponse(Guid UsuarioId, string? Email, Guid EmpresaId,
        string[] Roles, DateTimeOffset ExpiraEn);

    [Authorize]
    [HttpGet("me")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var sessionId = EnterpriseSecurity.SessionId(User);
        if (!int.TryParse(User.FindFirstValue(EnterpriseSecurity.RefreshVersionClaim), out var version))
            return Unauthorized();
        // Una sola lectura: identidad y empresa proceden únicamente de la sesión autenticada.
        var current = await (from session in db.SesionesAcceso.AsNoTracking()
            join user in db.Users.AsNoTracking() on session.UsuarioId equals user.Id
            join member in db.Membresias.AsNoTracking().Where(m => m.Activa)
                on new { session.UsuarioId, session.EmpresaId } equals new { member.UsuarioId, member.EmpresaId }
                into memberships
            from membership in memberships.DefaultIfEmpty()
            where session.Id == sessionId && session.UsuarioId == access.ActorId &&
                session.EmpresaId == access.EmpresaId && session.VersionRenovacion == version &&
                session.RevocadaEn == null && session.ExpiraEn > DateTimeOffset.UtcNow && user.Activo
            select new { user.Id, user.Email, session.EmpresaId, session.ExpiraEn,
                Rol = membership == null ? null : membership.Rol }).SingleOrDefaultAsync(ct);
        if (current is null) return Unauthorized();
        if (!ValidRole(current.Rol)) return Forbid();
        return Ok(new SessionResponse(current.Id, current.Email, current.EmpresaId,
            [current.Rol!], current.ExpiraEn));
    }

    [AllowAnonymous]
    [HttpGet("csrf")]
    public IActionResult Csrf() => Ok(new { token = antiforgery.GetAndStoreTokens(HttpContext).RequestToken });

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest input, CancellationToken ct)
    {
        var web = input.Cliente == "Web";
        if (!web && input.Cliente != "Android") return BadRequest();
        if (web && !await antiforgery.IsRequestValidAsync(HttpContext)) return BadRequest("CSRF inválido.");
        if (input.EmpresaId == Guid.Empty || string.IsNullOrWhiteSpace(input.Email) || input.Email.Length > 256 ||
            string.IsNullOrWhiteSpace(input.Password) || input.Password.Length > 256)
            return Unauthorized();
        var user = await users.FindByEmailAsync(input.Email);
        var valid = user is not null && user.Activo &&
            (await signIn.CheckPasswordSignInAsync(user, input.Password, lockoutOnFailure: true)).Succeeded;
        var membership = valid ? await db.Membresias.AsNoTracking().SingleOrDefaultAsync(m => m.EmpresaId == input.EmpresaId && m.UsuarioId == user!.Id && m.Activa, ct) : null;
        if (membership is null)
        {
            await AuditAsync(Guid.Empty, user?.Id, "LoginDenegado", null, ct);
            return Unauthorized();
        }
        var session = new SesionAcceso { Id = Guid.NewGuid(), UsuarioId = user!.Id, EmpresaId = membership.EmpresaId,
            ExpiraEn = DateTimeOffset.UtcNow.Add(web ? TimeSpan.FromHours(8) : TimeSpan.FromDays(7)) };
        db.SesionesAcceso.Add(session);
        await AuditAsync(membership.EmpresaId, user.Id, "Login", session.Id, ct);
        var principal = await PrincipalAsync(user, session);
        await HttpContext.SignInAsync(web ? EnterpriseSecurity.Web : EnterpriseSecurity.Android, principal,
            new AuthenticationProperties { IsPersistent = false });
        return web ? Ok(new { session = session.Id, empresaId = session.EmpresaId }) : new EmptyResult();
    }

    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.RefreshToken) || input.RefreshToken.Length > 8192) return Unauthorized();
        var ticket = bearer.Get(EnterpriseSecurity.Android).RefreshTokenProtector.Unprotect(input.RefreshToken);
        if (ticket?.Properties.ExpiresUtc is not { } expiry || expiry <= DateTimeOffset.UtcNow) return Unauthorized();
        var userId = EnterpriseSecurity.UserId(ticket.Principal);
        var company = EnterpriseSecurity.CompanyId(ticket.Principal);
        var sessionId = EnterpriseSecurity.SessionId(ticket.Principal);
        if (!int.TryParse(ticket.Principal.FindFirstValue(EnterpriseSecurity.RefreshVersionClaim), out var version)) return Unauthorized();
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null || !user.Activo || await users.IsLockedOutAsync(user) ||
            ticket.Principal.FindFirstValue(users.Options.ClaimsIdentity.SecurityStampClaimType) != await users.GetSecurityStampAsync(user) ||
            !await db.Membresias.AsNoTracking().AnyAsync(m => m.UsuarioId == userId && m.EmpresaId == company && m.Activa, ct)) return Unauthorized();
        // Renovación de uso único: solo una petición puede incrementar esta versión.
        var updated = await db.SesionesAcceso.Where(s => s.Id == sessionId && s.UsuarioId == userId && s.EmpresaId == company &&
            s.RevocadaEn == null && s.ExpiraEn > DateTimeOffset.UtcNow && s.VersionRenovacion == version)
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.VersionRenovacion, version + 1), ct);
        if (updated != 1) return Unauthorized();
        await AuditAsync(company, userId, "Renovacion", sessionId, ct);
        var session = new SesionAcceso { Id = sessionId, UsuarioId = userId, EmpresaId = company, VersionRenovacion = version + 1 };
        await HttpContext.SignInAsync(EnterpriseSecurity.Android, await PrincipalAsync(user, session));
        return new EmptyResult();
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var sessionId = EnterpriseSecurity.SessionId(User);
        await db.SesionesAcceso.Where(s => s.Id == sessionId && s.UsuarioId == access.ActorId && s.EmpresaId == access.EmpresaId)
            .ExecuteUpdateAsync(x => x.SetProperty(s => s.RevocadaEn, DateTimeOffset.UtcNow), ct);
        await AuditAsync(access.EmpresaId, access.ActorId, "Logout", sessionId, ct);
        if (!Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            await HttpContext.SignOutAsync(EnterpriseSecurity.Web);
        return NoContent();
    }

    [Authorize]
    [HttpPost("usuarios")]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest input, CancellationToken ct)
    {
        if (!await access.HasRoleAsync(ct, RolesEmpresa.Administrador)) return Forbid();
        if (string.IsNullOrWhiteSpace(input.Email) || input.Email.Length > 256 ||
            string.IsNullOrWhiteSpace(input.Password) || input.Password.Length > 256 || !ValidRole(input.Rol) ||
            !await DriverValidAsync(input.Rol!, input.ConductorId, ct)) return BadRequest();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try { await guard.AuthorizeAsync(access.EmpresaId, access.ActorId, "admin_users", null, ct); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        // Un conductor tiene como máximo un acceso (índice único); responder 409 claro en vez de 500.
        if (input.ConductorId is { } driver && await db.Membresias.AnyAsync(m => m.ConductorId == driver, ct))
            return Conflict("Ese conductor ya tiene un acceso.");
        var user = new AppUser { Id = Guid.NewGuid(), UserName = input.Email.Trim(), Email = input.Email.Trim(), Activo = true };
        var result = await users.CreateAsync(user, input.Password);
        if (!result.Succeeded) return BadRequest(result.Errors.Select(x => x.Code));
        db.Membresias.Add(new EmpresaMembresia { EmpresaId = access.EmpresaId, UsuarioId = user.Id, Rol = input.Rol!, ConductorId = input.ConductorId });
        await AuditAsync(access.EmpresaId, access.ActorId, "UsuarioCreado", user.Id, ct);
        await tx.CommitAsync(ct);
        return StatusCode(StatusCodes.Status201Created, new { id = user.Id });
    }

    [Authorize]
    [HttpPut("usuarios/{id:guid}")]
    public async Task<IActionResult> ChangeUser(Guid id, [FromBody] ChangeUserRequest input, CancellationToken ct)
    {
        if (!await access.HasRoleAsync(ct, RolesEmpresa.Administrador)) return Forbid();
        if (id == access.ActorId || !ValidRole(input.Rol) || !await DriverValidAsync(input.Rol!, input.ConductorId, ct)) return BadRequest();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try { await guard.AuthorizeAsync(access.EmpresaId, access.ActorId, "admin_users", null, ct); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        var membership = await db.Membresias.SingleOrDefaultAsync(m => m.EmpresaId == access.EmpresaId && m.UsuarioId == id, ct);
        if (membership is null) return NotFound();
        membership.Rol = input.Rol!;
        membership.ConductorId = input.ConductorId;
        membership.Activa = input.Activo;
        await db.SaveChangesAsync(ct);
        await db.SesionesAcceso.Where(s => s.EmpresaId == access.EmpresaId && s.UsuarioId == id && s.RevocadaEn == null)
            .ExecuteUpdateAsync(x => x.SetProperty(s => s.RevocadaEn, DateTimeOffset.UtcNow), ct);
        await AuditAsync(access.EmpresaId, access.ActorId, "PermisosModificados", id, ct);
        await tx.CommitAsync(ct);
        return NoContent();
    }

    private async Task<bool> DriverValidAsync(string role, Guid? driverId, CancellationToken ct) =>
        role == RolesEmpresa.Conductor
            ? driverId is Guid id && id != Guid.Empty && await db.Conductores.AnyAsync(c => c.EmpresaId == access.EmpresaId && c.Id == id, ct)
            : driverId is null;

    private static bool ValidRole(string? role) => role is RolesEmpresa.Administrador or RolesEmpresa.Operador or RolesEmpresa.Conductor;

    private async Task<ClaimsPrincipal> PrincipalAsync(AppUser user, SesionAcceso session)
    {
        var principal = await signIn.CreateUserPrincipalAsync(user);
        var identity = (ClaimsIdentity)principal.Identity!;
        identity.AddClaim(new Claim(EnterpriseSecurity.CompanyClaim, session.EmpresaId.ToString()));
        identity.AddClaim(new Claim(EnterpriseSecurity.SessionClaim, session.Id.ToString()));
        identity.AddClaim(new Claim(EnterpriseSecurity.RefreshVersionClaim, session.VersionRenovacion.ToString()));
        return principal;
    }

    private async Task AuditAsync(Guid company, Guid? user, string type, Guid? obj, CancellationToken ct)
    {
        db.AuditoriaAcceso.Add(new AuditoriaAcceso { EmpresaId = company, UsuarioId = user, Fecha = DateTimeOffset.UtcNow, Tipo = type, ObjetoId = obj });
        await db.SaveChangesAsync(ct);
    }
}
