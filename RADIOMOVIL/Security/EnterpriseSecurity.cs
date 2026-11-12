using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using System.Security.Cryptography.X509Certificates;
using RadioMovil.Infrastructure;

namespace RADIOMOVIL.Security;

public static class EnterpriseSecurity
{
    public const string Web = "rm.web";
    public const string Android = "rm.android";
    public const string Client = "rm.client";
    public const string SessionClaim = "rm.session";
    public const string CompanyClaim = "rm.empresa";
    public const string RefreshVersionClaim = "rm.refresh_version";

    public static IServiceCollection AddEnterpriseSecurity(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddIdentityCore<AppUser>(o =>
        {
            o.Password.RequiredLength = 12;
            o.Password.RequireNonAlphanumeric = true;
            o.Password.RequireDigit = true;
            o.Password.RequireUppercase = true;
            o.Lockout.MaxFailedAccessAttempts = 5;
            o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            o.User.RequireUniqueEmail = true;
        }).AddEntityFrameworkStores<RadioMovilDbContext>().AddSignInManager();
        var keys = services.AddDataProtection().SetApplicationName("RadioMovil.Testing")
            .PersistKeysToDbContext<RadioMovilDbContext>();
        var certPath = Environment.GetEnvironmentVariable("RADIOMOVIL_DP_CERT_PATH");
        var certPassword = Environment.GetEnvironmentVariable("RADIOMOVIL_DP_CERT_PASSWORD");
        if (!string.IsNullOrWhiteSpace(certPath) && !string.IsNullOrWhiteSpace(certPassword))
            keys.ProtectKeysWithCertificate(X509CertificateLoader.LoadPkcs12FromFile(certPath, certPassword));
        else if (OperatingSystem.IsWindows())
            keys.ProtectKeysWithDpapi(protectToLocalMachine: true);
        else throw new InvalidOperationException("Se requiere certificado externo para cifrar claves Data Protection fuera de Windows.");
        services.AddAuthentication(o =>
        {
            o.DefaultScheme = Client;
            o.DefaultChallengeScheme = Client;
        }).AddPolicyScheme(Client, null, o =>
        {
            o.ForwardDefaultSelector = ctx =>
                ctx.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? Android : Web;
        }).AddCookie(Web, o =>
        {
            o.Cookie.Name = "rm.web";
            o.Cookie.HttpOnly = true;
            o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.ExpireTimeSpan = TimeSpan.FromHours(8);
            o.SlidingExpiration = false;
            o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
            o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
        }).AddBearerToken(Android, o =>
        {
            o.BearerTokenExpiration = TimeSpan.FromMinutes(10);
            o.RefreshTokenExpiration = TimeSpan.FromDays(7);
        });
        services.AddAntiforgery(o =>
        {
            o.HeaderName = "X-CSRF-TOKEN";
            o.Cookie.Name = "rm.csrf";
            o.Cookie.HttpOnly = true;
            o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            o.Cookie.SameSite = SameSiteMode.Strict;
        });
        services.AddAuthorization();
        services.AddScoped<AccessScope>();
        services.AddScoped<IDespachoGuard, DespachoGuard>();
        return services;
    }

    public static async Task EnforceSessionAndCsrfAsync(HttpContext ctx, RequestDelegate next)
    {
        if (ctx.User.Identity?.IsAuthenticated == true)
        {
            var scope = ctx.RequestServices.GetRequiredService<AccessScope>();
            // Permitir recuperar el formulario cuando una cookie vieja aún puede
            // descifrarse pero su sesión fue revocada. El login/refresh se validan allí.
            var isCredentialEndpoint = ctx.Request.Path is { } path &&
                (path == "/api/v1/auth/login" || path == "/api/v1/auth/csrf" || path == "/api/v1/auth/refresh");
            if (!isCredentialEndpoint && !await scope.SessionValidAsync(ctx.RequestAborted))
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
            if (!HttpMethods.IsGet(ctx.Request.Method) && !HttpMethods.IsHead(ctx.Request.Method) &&
                !HttpMethods.IsOptions(ctx.Request.Method) &&
                !ctx.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                try { await ctx.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(ctx); }
                catch (AntiforgeryValidationException) { ctx.Response.StatusCode = 400; return; }
            }
        }
        await next(ctx);
    }

    public static Guid UserId(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
    public static Guid CompanyId(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(CompanyClaim), out var id) ? id : Guid.Empty;
    public static Guid SessionId(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(SessionClaim), out var id) ? id : Guid.Empty;
}

public sealed class AccessScope(RadioMovilDbContext db, IHttpContextAccessor accessor)
{
    public Guid EmpresaId => EnterpriseSecurity.CompanyId(accessor.HttpContext?.User ?? new ClaimsPrincipal());
    public Guid ActorId => EnterpriseSecurity.UserId(accessor.HttpContext?.User ?? new ClaimsPrincipal());

    public async Task<bool> SessionValidAsync(CancellationToken ct)
    {
        var ctx = accessor.HttpContext;
        if (ctx?.User.Identity?.IsAuthenticated != true) return false;
        var id = EnterpriseSecurity.SessionId(ctx.User);
        var userId = EnterpriseSecurity.UserId(ctx.User);
        var empresa = EnterpriseSecurity.CompanyId(ctx.User);
        var version = int.TryParse(ctx.User.FindFirstValue(EnterpriseSecurity.RefreshVersionClaim), out var parsed) ? parsed : -1;
        return id != Guid.Empty && userId != Guid.Empty && empresa != Guid.Empty &&
            await db.SesionesAcceso.AsNoTracking().AnyAsync(s => s.Id == id && s.UsuarioId == userId &&
                s.EmpresaId == empresa && s.VersionRenovacion == version && s.RevocadaEn == null && s.ExpiraEn > DateTimeOffset.UtcNow, ct) &&
            await db.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.Activo, ct);
    }

    public Task<EmpresaMembresia?> MembershipAsync(CancellationToken ct) =>
        db.Membresias.AsNoTracking().SingleOrDefaultAsync(m => m.EmpresaId == EmpresaId && m.UsuarioId == ActorId && m.Activa, ct);

    public async Task<bool> HasRoleAsync(CancellationToken ct, params string[] roles) =>
        await MembershipAsync(ct) is { } m && roles.Contains(m.Rol, StringComparer.Ordinal);
}

// Se ejecuta EN la transacción y antes del resultado idempotente. FOR SHARE
// bloquea desactivaciones/revocaciones hasta el COMMIT de la transición.
public sealed class DespachoGuard(RadioMovilDbContext db, IHttpContextAccessor accessor) : IDespachoGuard
{
    public async Task AuthorizeAsync(Guid empresaId, Guid actorId, string operation, Guid? target, CancellationToken ct)
    {
        var context = accessor.HttpContext;
        if (context is null) return; // solo proceso interno (Workers), sin solicitud HTTP
        var session = EnterpriseSecurity.SessionId(context.User);
        if (actorId != EnterpriseSecurity.UserId(context.User) || empresaId != EnterpriseSecurity.CompanyId(context.User) || session == Guid.Empty)
            throw new UnauthorizedAccessException();
        await using var cmd = db.Database.GetDbConnection().CreateCommand();
        cmd.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
        cmd.CommandText = """
            SELECT m."Rol", m."ConductorId" FROM membresias_empresa m
            JOIN usuarios_identity u ON u."Id" = m."UsuarioId"
            JOIN sesiones_acceso s ON s."UsuarioId" = u."Id" AND s."EmpresaId" = m."EmpresaId"
            WHERE m."EmpresaId" = @empresa AND m."UsuarioId" = @user AND s."Id" = @session
              AND u."Activo" AND m."Activa" AND s."RevocadaEn" IS NULL AND s."ExpiraEn" > clock_timestamp()
            FOR SHARE OF m, u, s
            """;
        foreach (var (name, value) in new[] { ("empresa", empresaId), ("user", actorId), ("session", session) })
        {
            var p = cmd.CreateParameter(); p.ParameterName = name; p.Value = value; cmd.Parameters.Add(p);
        }
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new UnauthorizedAccessException();
        var role = reader.GetString(0);
        var driverId = reader.IsDBNull(1) ? (Guid?)null : reader.GetGuid(1);
        await reader.DisposeAsync();
        if (operation == GestionFlota.OperacionConductorPropio)
        {
            // El conductor solo actúa sobre sí mismo: el objetivo debe ser SU conductor de membresía.
            if (role != RolesEmpresa.Conductor || driverId is null || target != driverId) throw new UnauthorizedAccessException();
        }
        else if (operation is "aceptar" or "rechazar" or "cancelar_conductor")
        {
            if (role != RolesEmpresa.Conductor || driverId is null || target is null) throw new UnauthorizedAccessException();
            var mine = operation == "cancelar_conductor"
                ? await db.Viajes.AsNoTracking().AnyAsync(v => v.Id == target && v.EmpresaId == empresaId && v.ConductorId == driverId, ct)
                : await db.Intentos.AsNoTracking().AnyAsync(i => i.Id == target && i.EmpresaId == empresaId && i.ConductorId == driverId, ct);
            if (!mine) throw new UnauthorizedAccessException();
        }
        else if (operation is "admin_users" or GestionFlota.OperacionAdministrar ? role != RolesEmpresa.Administrador : role is not (RolesEmpresa.Operador or RolesEmpresa.Administrador))
            throw new UnauthorizedAccessException();
    }
}
