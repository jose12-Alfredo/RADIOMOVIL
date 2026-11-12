using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using RadioMovil.Domain;

namespace RadioMovil.Infrastructure;

public sealed class RadioMovilDbContext(DbContextOptions<RadioMovilDbContext> options) : IdentityDbContext<AppUser, Microsoft.AspNetCore.Identity.IdentityRole<Guid>, Guid>(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
    public DbSet<EmpresaMembresia> Membresias => Set<EmpresaMembresia>();
    public DbSet<SesionAcceso> SesionesAcceso => Set<SesionAcceso>();
    public DbSet<AuditoriaAcceso> AuditoriaAcceso => Set<AuditoriaAcceso>();
    public DbSet<Solicitud> Solicitudes => Set<Solicitud>();
    public DbSet<Conductor> Conductores => Set<Conductor>();
    public DbSet<Vehiculo> Vehiculos => Set<Vehiculo>();
    public DbSet<Turno> Turnos => Set<Turno>();
    public DbSet<IntentoDespacho> Intentos => Set<IntentoDespacho>();
    public DbSet<Viaje> Viajes => Set<Viaje>();
    public DbSet<SolicitudEvento> Eventos => Set<SolicitudEvento>();
    public DbSet<ContadorCentral> Contadores => Set<ContadorCentral>();
    public DbSet<ClaveIdempotencia> Idempotencias => Set<ClaveIdempotencia>();
    public DbSet<OperacionDespacho> OperacionesDespacho => Set<OperacionDespacho>();
    public DbSet<EventoOutbox> Outbox => Set<EventoOutbox>();
    public DbSet<IncidenciaWorker> IncidenciasWorker => Set<IncidenciaWorker>();
    public DbSet<UbicacionConductor> Ubicaciones => Set<UbicacionConductor>();
    public DbSet<Central> Centrales => Set<Central>();
    public DbSet<OperacionFlota> OperacionesFlota => Set<OperacionFlota>();
    public DbSet<UbicacionHistorial> UbicacionesHistorial => Set<UbicacionHistorial>();
    public DbSet<AutorizacionVehiculo> AutorizacionesVehiculo => Set<AutorizacionVehiculo>();
    public DbSet<ViajeEvento> ViajeEventos => Set<ViajeEvento>();
    /// <summary>Misma lista que <see cref="Viaje.Activos"/> y que validar_exclusividad_despacho().</summary>
    public const string ViajeActivoFiltro = "\"Estado\" IN ('Asignado', 'EnCamino', 'EnPuerta', 'EnCurso')";

    protected override void OnModelCreating(ModelBuilder m)
    {
        base.OnModelCreating(m);
        m.Entity<AppUser>(e => { e.ToTable("usuarios_identity"); e.Property(x => x.Activo).IsRequired(); });
        m.Entity<Microsoft.AspNetCore.Identity.IdentityRole<Guid>>().ToTable("roles_identity");
        m.Entity<Microsoft.AspNetCore.Identity.IdentityUserClaim<Guid>>().ToTable("claims_usuario_identity");
        m.Entity<Microsoft.AspNetCore.Identity.IdentityUserRole<Guid>>().ToTable("roles_usuario_identity");
        m.Entity<Microsoft.AspNetCore.Identity.IdentityUserLogin<Guid>>().ToTable("logins_identity");
        m.Entity<Microsoft.AspNetCore.Identity.IdentityRoleClaim<Guid>>().ToTable("claims_rol_identity");
        m.Entity<Microsoft.AspNetCore.Identity.IdentityUserToken<Guid>>().ToTable("tokens_usuario_identity");
        m.Entity<DataProtectionKey>().ToTable("claves_proteccion_datos");
        m.Entity<EmpresaMembresia>(e => { e.ToTable("membresias_empresa", t => t.HasCheckConstraint("CK_membresia_rol_conductor", "(\"Rol\" = 'Conductor' AND \"ConductorId\" IS NOT NULL) OR (\"Rol\" IN ('Administrador','Operador') AND \"ConductorId\" IS NULL)")); e.HasKey(x => new { x.EmpresaId, x.UsuarioId }); e.Property(x => x.Rol).HasMaxLength(20).IsRequired(); e.HasIndex(x => x.ConductorId).HasFilter("\"ConductorId\" IS NOT NULL").IsUnique(); e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.Restrict); e.HasOne<Conductor>().WithMany().HasForeignKey(x => new { x.EmpresaId, x.ConductorId }).HasPrincipalKey(x => new { x.EmpresaId, x.Id }).OnDelete(DeleteBehavior.Restrict); });
        m.Entity<SesionAcceso>(e => { e.ToTable("sesiones_acceso"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.UsuarioId, x.RevocadaEn }); e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.Restrict); e.HasOne<EmpresaMembresia>().WithMany().HasForeignKey(x => new { x.EmpresaId, x.UsuarioId }).OnDelete(DeleteBehavior.Restrict); });
        m.Entity<AuditoriaAcceso>(e => { e.ToTable("auditoria_accesos"); e.HasKey(x => x.Id); e.Property(x => x.Tipo).HasMaxLength(80); e.HasIndex(x => new { x.EmpresaId, x.Fecha }); });
        m.HasPostgresExtension("postgis");
        m.Entity<SolicitudEvento>().Property<Guid>("EmpresaId").IsRequired();
        m.Entity<Solicitud>(e =>
        {
            e.ToTable("solicitudes"); e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Origen).HasMaxLength(500).IsRequired();
            e.Property(x => x.Estado).HasConversion<string>().HasMaxLength(24);
            e.Property<long>("OrdenRecepcion").IsRequired();
            e.Property<Guid>("CentralId").IsRequired();
            e.Property<string?>("TelefonoPasajero").HasMaxLength(40);
            e.Property<string?>("NombrePasajero").HasMaxLength(160);
            e.Property<string?>("Destino").HasMaxLength(500);
            e.Property<string?>("Observaciones").HasMaxLength(1000);
            e.Property<Point?>("PuntoRecogida").HasColumnType("geometry(Point,4326)");
            e.Property<Point?>("PuntoDestino").HasColumnType("geometry(Point,4326)");
            e.HasOne<Central>().WithMany().HasForeignKey("EmpresaId", "CentralId").HasPrincipalKey(x => new { x.EmpresaId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.Property<uint>("xmin").IsRowVersion();
            e.HasIndex(x => new { x.EmpresaId, x.Id }).IsUnique();
            e.HasIndex("EmpresaId", "CentralId", "OrdenRecepcion").IsUnique();
            e.HasMany(x => x.Intentos).WithOne().HasForeignKey(x => new { x.EmpresaId, x.SolicitudId }).HasPrincipalKey(x => new { x.EmpresaId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Eventos).WithOne().HasForeignKey("EmpresaId", "SolicitudId").HasPrincipalKey(x => new { x.EmpresaId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.Navigation(x => x.Intentos).UsePropertyAccessMode(PropertyAccessMode.Field);
            e.Navigation(x => x.Eventos).UsePropertyAccessMode(PropertyAccessMode.Field);
        });
        m.Entity<Central>(e => { e.ToTable("centrales"); e.HasKey(x => new { x.EmpresaId, x.Id }); e.Property(x => x.Id).ValueGeneratedNever(); e.Property(x => x.Nombre).HasMaxLength(160).IsRequired(); e.HasIndex(x => new { x.EmpresaId, x.Nombre }).IsUnique(); });
        m.Entity<Conductor>(e => { e.ToTable("conductores"); e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever(); e.Property(x => x.Nombre).HasMaxLength(160); e.HasIndex(x => new { x.EmpresaId, x.Id }).IsUnique(); });
        m.Entity<Vehiculo>(e => { e.ToTable("vehiculos"); e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever(); e.Property(x => x.Patente).HasMaxLength(32); e.HasIndex(x => new { x.EmpresaId, x.Id }).IsUnique(); e.HasIndex(x => new { x.EmpresaId, x.Patente }).IsUnique(); });
        m.Entity<IntentoDespacho>(e =>
        {
            e.ToTable("intentos_despacho", t => t.HasCheckConstraint("CK_intento_vencimiento", "\"VenceEn\" > \"CreadoEn\""));
            e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever(); e.Property(x => x.Estado).HasConversion<string>().HasMaxLength(24);
            e.HasOne<Conductor>().WithMany().HasForeignKey(x => new { x.EmpresaId, x.ConductorId }).HasPrincipalKey(x => new { x.EmpresaId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.SolicitudId).IsUnique().HasFilter("\"Estado\" = 'Ofertado'");
            e.HasIndex(x => x.ConductorId).IsUnique().HasFilter("\"Estado\" = 'Ofertado'");
        });
        m.Entity<Viaje>(e =>
        {
            e.ToTable("viajes"); e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever(); e.Property(x => x.Estado).HasConversion<string>().HasMaxLength(24);
            e.HasOne<Solicitud>().WithMany().HasForeignKey(x => new { x.EmpresaId, x.SolicitudId }).HasPrincipalKey(x => new { x.EmpresaId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Conductor>().WithMany().HasForeignKey(x => new { x.EmpresaId, x.ConductorId }).HasPrincipalKey(x => new { x.EmpresaId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.SolicitudId).IsUnique().HasFilter(ViajeActivoFiltro);
            e.HasIndex(x => x.ConductorId).IsUnique().HasFilter(ViajeActivoFiltro);
        });
        m.Entity<ViajeEvento>(e =>
        {
            e.ToTable("viaje_eventos", t => t.HasCheckConstraint("CK_viaje_evento_tipo", "\"Tipo\" IN ('EnCamino', 'EnPuerta', 'EnCurso', 'Finalizado')"));
            e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Tipo).HasMaxLength(24); e.Property(x => x.Observaciones).HasMaxLength(500);
            e.Property(x => x.Posicion).HasColumnType("geometry(Point,4326)");
            e.HasIndex(x => new { x.ViajeId, x.Tipo }).IsUnique();
            e.HasOne<Viaje>().WithMany().HasForeignKey(x => x.ViajeId).OnDelete(DeleteBehavior.Restrict);
        });
        m.Entity<Turno>(e =>
        {
            e.ToTable("turnos", t => t.HasCheckConstraint("CK_turno_fechas", "\"Fin\" IS NULL OR \"Fin\" >= \"Inicio\"")); e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever();
            e.HasOne<Conductor>().WithMany().HasForeignKey(x => new { x.EmpresaId, x.ConductorId }).HasPrincipalKey(x => new { x.EmpresaId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Vehiculo>().WithMany().HasForeignKey(x => new { x.EmpresaId, x.VehiculoId }).HasPrincipalKey(x => new { x.EmpresaId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.ConductorId).IsUnique().HasFilter("\"Fin\" IS NULL");
            e.HasIndex(x => x.VehiculoId).IsUnique().HasFilter("\"Fin\" IS NULL");
            e.HasIndex(x => new { x.EmpresaId, x.Inicio });
            e.Property(x => x.MotivoCierre).HasMaxLength(500);
        });
        m.Entity<OperacionFlota>(e => { e.ToTable("operaciones_flota"); e.HasKey(x => new { x.EmpresaId, x.Ambito, x.Clave }); e.Property(x => x.Ambito).HasMaxLength(40); e.Property(x => x.Clave).HasMaxLength(160); e.Property(x => x.Huella).HasMaxLength(64); });
        m.Entity<SolicitudEvento>(e => { e.ToTable("solicitud_eventos"); e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever(); e.Property<Guid>("EmpresaId").IsRequired(); e.Property(x => x.Tipo).HasMaxLength(80); e.Property(x => x.Motivo).HasMaxLength(1000); e.HasIndex(x => new { x.SolicitudId, x.Fecha }); });
        m.Entity<ContadorCentral>(e => { e.ToTable("contadores_central"); e.HasKey(x => new { x.EmpresaId, x.CentralId }); });
        m.Entity<ClaveIdempotencia>(e => { e.ToTable("claves_idempotencia"); e.HasKey(x => new { x.EmpresaId, x.Ambito, x.Clave }); e.Property(x => x.Ambito).HasMaxLength(100); e.Property(x => x.Clave).HasMaxLength(160); });
        m.Entity<OperacionDespacho>(e => { e.ToTable("operaciones_despacho"); e.HasKey(x => new { x.EmpresaId, x.Ambito, x.Clave }); e.Property(x => x.Ambito).HasMaxLength(40); e.Property(x => x.Clave).HasMaxLength(160); e.Property(x => x.Huella).HasMaxLength(64); e.Property(x => x.Estado).HasConversion<string>().HasMaxLength(24); });
        m.Entity<EventoOutbox>(e => { e.ToTable("outbox"); e.HasKey(x => x.Id); e.Property(x => x.Tipo).HasMaxLength(160); e.Property(x => x.Contenido).HasColumnType("jsonb"); e.HasIndex(x => new { x.PublicadoEn, x.CreadoEn }); });
        m.Entity<IncidenciaWorker>(e => { e.ToTable("incidencias_worker"); e.HasKey(x => new { x.EmpresaId, x.Tipo, x.ReferenciaId }); e.Property(x => x.Tipo).HasMaxLength(80); e.HasIndex(x => new { x.AtendidaEn, x.ProximoIntento }); });
        m.Entity<IntentoDespacho>().HasIndex(x => new { x.VenceEn, x.Id }).HasFilter("\"Estado\" = 'Ofertado'");
        m.Entity<UbicacionConductor>(e =>
        {
            e.ToTable("ubicaciones_conductor", t => t.HasCheckConstraint("CK_ubicacion_precision", "\"PrecisionMetros\" > 0"));
            e.HasKey(x => x.ConductorId); e.Property(x => x.Posicion).HasColumnType("geometry(Point,4326)"); e.HasIndex(x => x.Posicion).HasMethod("gist");
            e.HasOne<Conductor>().WithOne().HasForeignKey<UbicacionConductor>(x => x.ConductorId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Conductor>().WithMany().HasForeignKey(x => new { x.EmpresaId, x.ConductorId }).HasPrincipalKey(x => new { x.EmpresaId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Turno>().WithMany().HasForeignKey(x => x.TurnoId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.EmpresaId, x.RegistradoEn });
        });
        m.Entity<UbicacionHistorial>(e =>
        {
            e.ToTable("ubicaciones_historial", t => t.HasCheckConstraint("CK_historial_precision", "\"PrecisionMetros\" > 0"));
            e.HasKey(x => x.Id); e.Property(x => x.Id).UseIdentityAlwaysColumn(); e.Property(x => x.Posicion).HasColumnType("geometry(Point,4326)");
            e.HasOne<Conductor>().WithMany().HasForeignKey(x => new { x.EmpresaId, x.ConductorId }).HasPrincipalKey(x => new { x.EmpresaId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Turno>().WithMany().HasForeignKey(x => x.TurnoId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.EmpresaId, x.ConductorId, x.RecibidaEn });
        });
        m.Entity<AutorizacionVehiculo>(e =>
        {
            e.ToTable("conductor_vehiculos", t => t.HasCheckConstraint("CK_autorizacion_revocacion", "(\"RevocadaEn\" IS NULL) = (\"RevocadaPor\" IS NULL)"));
            e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever();
            e.HasOne<Conductor>().WithMany().HasForeignKey(x => new { x.EmpresaId, x.ConductorId }).HasPrincipalKey(x => new { x.EmpresaId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Vehiculo>().WithMany().HasForeignKey(x => new { x.EmpresaId, x.VehiculoId }).HasPrincipalKey(x => new { x.EmpresaId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.ConductorId, x.VehiculoId }).IsUnique().HasFilter("\"RevocadaEn\" IS NULL");
        });
    }
}

public sealed class ContadorCentral { public Guid EmpresaId { get; set; } public Guid CentralId { get; set; } public long UltimoOrden { get; set; } }
public sealed class Central { public Guid Id { get; set; } public Guid EmpresaId { get; set; } public string Nombre { get; set; } = ""; public bool Activa { get; set; } = true; }
public sealed class ClaveIdempotencia { public Guid EmpresaId { get; set; } public string Ambito { get; set; } = ""; public string Clave { get; set; } = ""; public Guid SolicitudId { get; set; } }
public sealed class OperacionDespacho
{
    public Guid EmpresaId { get; set; }
    public string Ambito { get; set; } = "";
    public string Clave { get; set; } = "";
    public string Huella { get; set; } = "";
    public Guid SolicitudId { get; set; }
    public Guid? IntentoId { get; set; }
    public Guid? ViajeId { get; set; }
    public EstadoSolicitud? Estado { get; set; }
    public DateTimeOffset? Fecha { get; set; }
}
/// <summary>Resultado idempotente de altas de flota y apertura/cierre de turnos.</summary>
public sealed class OperacionFlota { public Guid EmpresaId { get; set; } public string Ambito { get; set; } = ""; public string Clave { get; set; } = ""; public string Huella { get; set; } = ""; public Guid RecursoId { get; set; } public DateTimeOffset Fecha { get; set; } }
public sealed class EventoOutbox { public Guid Id { get; set; } public Guid EmpresaId { get; set; } public string Tipo { get; set; } = ""; public string Contenido { get; set; } = "{}"; public DateTimeOffset CreadoEn { get; set; } public DateTimeOffset? PublicadoEn { get; set; } }
public sealed class IncidenciaWorker
{
    public Guid EmpresaId { get; set; }
    public string Tipo { get; set; } = "";
    public Guid ReferenciaId { get; set; }
    public DateTimeOffset PrimeraDeteccion { get; set; }
    public DateTimeOffset UltimaDeteccion { get; set; }
    public DateTimeOffset ProximoIntento { get; set; }
    public DateTimeOffset? AtendidaEn { get; set; }
    public int Intentos { get; set; }
}
/// <summary>Última ubicación REAL recibida por conductor. RegistradoEn es la fecha de recepción según el reloj de PostgreSQL.</summary>
public sealed class UbicacionConductor
{
    public Guid ConductorId { get; set; }
    public Guid EmpresaId { get; set; }
    public Guid TurnoId { get; set; }
    public Point Posicion { get; set; } = null!;
    public double PrecisionMetros { get; set; }
    public DateTimeOffset CapturadaEn { get; set; }
    public DateTimeOffset RegistradoEn { get; set; }
}
/// <summary>Historial de solo inserción de cada ubicación aceptada.</summary>
public sealed class UbicacionHistorial
{
    public long Id { get; set; }
    public Guid EmpresaId { get; set; }
    public Guid ConductorId { get; set; }
    public Guid TurnoId { get; set; }
    public Point Posicion { get; set; } = null!;
    public double PrecisionMetros { get; set; }
    public DateTimeOffset CapturadaEn { get; set; }
    public DateTimeOffset RecibidaEn { get; set; }
}
/// <summary>Vehículos con los que un conductor puede iniciar turno por sí mismo. La revocación conserva la fila.</summary>
public sealed class AutorizacionVehiculo
{
    public Guid Id { get; set; }
    public Guid EmpresaId { get; set; }
    public Guid ConductorId { get; set; }
    public Guid VehiculoId { get; set; }
    public DateTimeOffset AutorizadaEn { get; set; }
    public Guid AutorizadaPor { get; set; }
    public DateTimeOffset? RevocadaEn { get; set; }
    public Guid? RevocadaPor { get; set; }
}

public static class RegistroPersistencia
{
    public static IServiceCollection AddPersistencia(this IServiceCollection services, string connectionString, GpsOptions? gps = null) =>
        services.AddSingleton(gps ?? new GpsOptions()).AddScoped<RegistroTiempos>()
            .AddDbContext<RadioMovilDbContext>(options => options.UseNpgsql(NeonConnection.Validate(connectionString), npgsql => npgsql.UseNetTopologySuite()))
            .AddScoped<RegistroSolicitudes>()
            .AddScoped<GestionFlota>()
            .AddScoped<RecepcionGps>()
            .AddScoped<Carreras>()
            .AddScoped<PasosViaje>()
            .AddScoped<RadioMovil.Application.IDespachoManual, DespachoManualPostgres>();
}
/// <summary>
/// Cada paso del conductor (decisión 2026-11-10): hora del servidor y la última posición GPS ACEPTADA de su turno en ese momento
/// (null si no había; nunca se inventa). Un paso por viaje y tipo: el reintento devuelve el mismo registro.
/// </summary>
public sealed class ViajeEvento
{
    public Guid Id { get; set; }
    public Guid EmpresaId { get; set; }
    public Guid ViajeId { get; set; }
    public string Tipo { get; set; } = null!;
    public DateTimeOffset En { get; set; }
    public Guid ActorId { get; set; }
    public Point? Posicion { get; set; }
    public double? PrecisionMetros { get; set; }
    public DateTimeOffset? GpsRegistradoEn { get; set; }
    public string? Observaciones { get; set; }
}
