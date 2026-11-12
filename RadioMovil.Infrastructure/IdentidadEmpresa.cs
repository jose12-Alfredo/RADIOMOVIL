using Microsoft.AspNetCore.Identity;

namespace RadioMovil.Infrastructure;

public sealed class AppUser : IdentityUser<Guid>
{
    public bool Activo { get; set; } = true;
}

public static class RolesEmpresa
{
    public const string Administrador = "Administrador";
    public const string Operador = "Operador";
    public const string Conductor = "Conductor";
}

public sealed class EmpresaMembresia
{
    public Guid EmpresaId { get; set; }
    public Guid UsuarioId { get; set; }
    public string Rol { get; set; } = "";
    public Guid? ConductorId { get; set; }
    public bool Activa { get; set; } = true;
}

public sealed class SesionAcceso
{
    public Guid Id { get; set; }
    public Guid UsuarioId { get; set; }
    public Guid EmpresaId { get; set; }
    public DateTimeOffset ExpiraEn { get; set; }
    public DateTimeOffset? RevocadaEn { get; set; }
    public int VersionRenovacion { get; set; }
}

public sealed class AuditoriaAcceso
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EmpresaId { get; set; }
    public Guid? UsuarioId { get; set; }
    public DateTimeOffset Fecha { get; set; }
    public string Tipo { get; set; } = "";
    public Guid? ObjetoId { get; set; }
}
