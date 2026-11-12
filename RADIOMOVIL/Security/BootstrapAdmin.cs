using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RadioMovil.Infrastructure;

namespace RADIOMOVIL.Security;

public static class BootstrapAdmin
{
    // Operación local de Testing, nunca ruta HTTP. Credenciales suministradas
    // por el proceso; no se imprimen ni se guardan en texto claro.
    public static async Task RunIfRequestedAsync(IServiceProvider services, CancellationToken ct)
    {
        if (Environment.GetEnvironmentVariable("RADIOMOVIL_BOOTSTRAP_ADMIN") != "1") return;
        var email = Environment.GetEnvironmentVariable("RADIOMOVIL_BOOTSTRAP_EMAIL");
        var password = Environment.GetEnvironmentVariable("RADIOMOVIL_BOOTSTRAP_PASSWORD");
        if (!Guid.TryParse(Environment.GetEnvironmentVariable("RADIOMOVIL_BOOTSTRAP_EMPRESA_ID"), out var company) || company == Guid.Empty ||
            string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("Bootstrap: empresa, email y contraseña externos obligatorios.");
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioMovilDbContext>();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Serializa inicios concurrentes sin crear privilegios duplicados.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({company.ToString()}, 0))", ct);
        if (await db.Membresias.AnyAsync(m => m.EmpresaId == company && m.Rol == RolesEmpresa.Administrador, ct))
            throw new InvalidOperationException("Bootstrap rechazado: la empresa ya tiene administrador.");
        var user = new AppUser { Id = Guid.NewGuid(), UserName = email.Trim(), Email = email.Trim() };
        var created = await manager.CreateAsync(user, password);
        if (!created.Succeeded) throw new InvalidOperationException("Bootstrap rechazado: datos inválidos o usuario duplicado.");
        db.Membresias.Add(new EmpresaMembresia { EmpresaId = company, UsuarioId = user.Id, Rol = RolesEmpresa.Administrador });
        db.AuditoriaAcceso.Add(new AuditoriaAcceso { EmpresaId = company, UsuarioId = user.Id, Fecha = DateTimeOffset.UtcNow, Tipo = "BootstrapAdmin", ObjetoId = user.Id });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        Environment.SetEnvironmentVariable("RADIOMOVIL_BOOTSTRAP_PASSWORD", null);
    }
}
