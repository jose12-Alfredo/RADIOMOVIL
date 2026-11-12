using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using RadioMovil.Domain;

namespace RadioMovil.Infrastructure;

public enum EstadoConductor { FueraDeServicio, Disponible, Ocupado }

public sealed class FlotaConflictoException(string message) : Exception(message);
public sealed class FlotaNoEncontradaException(string message) : Exception(message);

public sealed record ResultadoFlota(Guid Id, bool Repetida);
public sealed record TurnoActivoVista(Guid Id, Guid VehiculoId, string Patente, DateTimeOffset Inicio);
/// <summary>Ocupacion: "Oferta" (oferta pendiente de respuesta) o "Viaje" (asignado o en curso).</summary>
public sealed record ConductorVista(Guid Id, string Nombre, EstadoConductor Estado, string? Ocupacion, TurnoActivoVista? Turno, string? AccesoEmail = null);
public sealed record VehiculoVista(Guid Id, string Patente, Guid? TurnoId, Guid? ConductorId, string? ConductorNombre);
public sealed record AutorizacionVista(Guid Id, Guid ConductorId, string ConductorNombre, Guid VehiculoId, string Patente, bool VehiculoLibre, DateTimeOffset AutorizadaEn);
public sealed record TurnoVista(Guid Id, Guid ConductorId, string ConductorNombre, Guid VehiculoId, string Patente,
    DateTimeOffset Inicio, DateTimeOffset? Fin, string? MotivoCierre);

/// <summary>
/// Conductores, vehículos y turnos (Fase B). El conductor y el vehículo se asocian solo
/// mediante un turno abierto; los índices únicos parciales impiden dos turnos abiertos por
/// conductor o por vehículo. Cerrar un turno toma el mismo bloqueo por conductor que el
/// trigger de exclusividad de despacho, que a su vez exige turno abierto para ofertar.
/// </summary>
public sealed class GestionFlota(RadioMovilDbContext db, IDespachoGuard? guard = null)
{
    public const string OperacionAdministrar = "admin_flota";
    public const string OperacionTurnos = "flota_turnos";
    /// <summary>Rol Conductor y objetivo == su propio ConductorId (membresía), revalidado en la transacción.</summary>
    public const string OperacionConductorPropio = "conductor_propio";

    public Task<ResultadoFlota> RegistrarConductorAsync(Guid empresaId, Guid actorId, string? nombre, string clave, CancellationToken ct = default)
    {
        var conductor = new Conductor(empresaId, nombre!);
        return EjecutarAsync(empresaId, actorId, "conductor", OperacionAdministrar, clave, new { nombre = conductor.Nombre }, "ConductorRegistrado", _ =>
        {
            db.Conductores.Add(conductor);
            return Task.FromResult(conductor.Id);
        }, ct);
    }

    public Task<ResultadoFlota> RegistrarVehiculoAsync(Guid empresaId, Guid actorId, string? patente, string clave, CancellationToken ct = default)
    {
        var vehiculo = new Vehiculo(empresaId, patente!);
        return EjecutarAsync(empresaId, actorId, "vehiculo", OperacionAdministrar, clave, new { patente = vehiculo.Patente }, "VehiculoRegistrado", async _ =>
        {
            if (await db.Vehiculos.AnyAsync(v => v.EmpresaId == empresaId && v.Patente == vehiculo.Patente, ct))
                throw new FlotaConflictoException("Ya existe un vehículo con esa placa.");
            db.Vehiculos.Add(vehiculo);
            return vehiculo.Id;
        }, ct);
    }

    public Task<ResultadoFlota> AbrirTurnoAsync(Guid empresaId, Guid actorId, Guid conductorId, Guid vehiculoId, string clave, CancellationToken ct = default)
    {
        Identidad.Exigir(conductorId, nameof(conductorId)); Identidad.Exigir(vehiculoId, nameof(vehiculoId));
        return EjecutarAsync(empresaId, actorId, "abrir_turno", OperacionTurnos, clave, new { conductorId, vehiculoId }, "TurnoAbierto",
            now => AbrirAsync(empresaId, actorId, conductorId, vehiculoId, now, exigirAutorizacion: false, ct), ct);
    }

    /// <summary>El conductor inicia SU turno, solo con un vehículo que el administrador le autorizó.</summary>
    public Task<ResultadoFlota> AbrirTurnoPropioAsync(Guid empresaId, Guid actorId, Guid conductorId, Guid vehiculoId, string clave, CancellationToken ct = default)
    {
        Identidad.Exigir(conductorId, nameof(conductorId)); Identidad.Exigir(vehiculoId, nameof(vehiculoId));
        return EjecutarAsync(empresaId, actorId, "abrir_turno_propio", OperacionConductorPropio, clave, new { conductorId, vehiculoId }, "TurnoAbiertoPorConductor",
            now => AbrirAsync(empresaId, actorId, conductorId, vehiculoId, now, exigirAutorizacion: true, ct), ct, objetivo: conductorId);
    }

    private async Task<Guid> AbrirAsync(Guid empresaId, Guid actorId, Guid conductorId, Guid vehiculoId, DateTimeOffset now, bool exigirAutorizacion, CancellationToken ct)
    {
        if (!await db.Conductores.AnyAsync(c => c.EmpresaId == empresaId && c.Id == conductorId, ct))
            throw new FlotaNoEncontradaException("Conductor no encontrado en tu empresa.");
        if (!await db.Vehiculos.AnyAsync(v => v.EmpresaId == empresaId && v.Id == vehiculoId, ct))
            throw new FlotaNoEncontradaException("Vehículo no encontrado en tu empresa.");
        if (exigirAutorizacion && !await db.AutorizacionesVehiculo.AnyAsync(a => a.EmpresaId == empresaId &&
                a.ConductorId == conductorId && a.VehiculoId == vehiculoId && a.RevocadaEn == null, ct))
            throw new FlotaConflictoException("Ese vehículo no está autorizado para ti. Pide la autorización al administrador.");
        // Mensajes claros; los índices únicos parciales siguen siendo la garantía final.
        if (await db.Turnos.AnyAsync(t => t.ConductorId == conductorId && t.Fin == null, ct))
            throw new FlotaConflictoException("El conductor ya tiene un turno abierto.");
        if (await db.Turnos.AnyAsync(t => t.VehiculoId == vehiculoId && t.Fin == null, ct))
            throw new FlotaConflictoException("El vehículo ya está en un turno abierto.");
        var turno = new Turno(empresaId, conductorId, vehiculoId, now, actorId);
        db.Turnos.Add(turno);
        return turno.Id;
    }

    public Task<ResultadoFlota> CerrarTurnoAsync(Guid empresaId, Guid actorId, Guid turnoId, string? motivo, string clave, CancellationToken ct = default)
    {
        Identidad.Exigir(turnoId, nameof(turnoId));
        var reason = Motivo(motivo);
        return EjecutarAsync(empresaId, actorId, "cerrar_turno", OperacionTurnos, clave, new { turnoId, motivo = reason }, "TurnoCerrado",
            now => CerrarAsync(empresaId, actorId, turnoId, null, reason, now, ct), ct);
    }

    /// <summary>El conductor cierra SU turno abierto; mismas reglas que el cierre por la central.</summary>
    public Task<ResultadoFlota> CerrarTurnoPropioAsync(Guid empresaId, Guid actorId, Guid conductorId, string? motivo, string clave, CancellationToken ct = default)
    {
        Identidad.Exigir(conductorId, nameof(conductorId));
        var reason = Motivo(motivo);
        return EjecutarAsync(empresaId, actorId, "cerrar_turno_propio", OperacionConductorPropio, clave, new { conductorId, motivo = reason }, "TurnoCerradoPorConductor",
            now => CerrarAsync(empresaId, actorId, null, conductorId, reason, now, ct), ct, objetivo: conductorId);
    }

    private static string? Motivo(string? motivo)
    {
        var reason = string.IsNullOrWhiteSpace(motivo) ? null : motivo.Trim();
        if (reason is { Length: > 500 }) throw new ErrorDominio("El motivo admite hasta 500 caracteres.");
        return reason;
    }

    private async Task<Guid> CerrarAsync(Guid empresaId, Guid actorId, Guid? turnoId, Guid? conductorPropio, string? reason, DateTimeOffset now, CancellationToken ct)
    {
        {
            var turno = (turnoId is { } id
                ? await db.Turnos.FromSqlInterpolated($"SELECT * FROM turnos WHERE \"Id\" = {id} AND \"EmpresaId\" = {empresaId} FOR UPDATE").SingleOrDefaultAsync(ct)
                : await db.Turnos.FromSqlInterpolated($"SELECT * FROM turnos WHERE \"ConductorId\" = {conductorPropio!.Value} AND \"EmpresaId\" = {empresaId} AND \"Fin\" IS NULL FOR UPDATE").SingleOrDefaultAsync(ct))
                ?? throw new FlotaNoEncontradaException(turnoId is null ? "No tienes un turno abierto." : "Turno no encontrado en tu empresa.");
            if (turno.Fin is not null) throw new FlotaConflictoException("El turno ya está cerrado.");
            // Mismo bloqueo transaccional que validar_exclusividad_despacho(): una oferta
            // concurrente espera a este COMMIT y vuelve a comprobar el turno abierto.
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({turno.ConductorId}::text, 42))", ct);
            var conductorId = turno.ConductorId;
            if (await db.Intentos.AnyAsync(i => i.ConductorId == conductorId && i.Estado == EstadoIntento.Ofertado, ct) ||
                await db.Viajes.AnyAsync(v => v.ConductorId == conductorId && Viaje.Activos.Contains(v.Estado), ct))
                throw new FlotaConflictoException("El conductor tiene una oferta o un viaje activo; no se puede cerrar el turno.");
            turno.Cerrar(now, actorId, reason);
            return turno.Id;
        }
    }

    public Task<ResultadoFlota> AutorizarVehiculoAsync(Guid empresaId, Guid actorId, Guid conductorId, Guid vehiculoId, string clave, CancellationToken ct = default)
    {
        Identidad.Exigir(conductorId, nameof(conductorId)); Identidad.Exigir(vehiculoId, nameof(vehiculoId));
        return EjecutarAsync(empresaId, actorId, "autorizar_vehiculo", OperacionAdministrar, clave, new { conductorId, vehiculoId }, "VehiculoAutorizado", async now =>
        {
            if (!await db.Conductores.AnyAsync(c => c.EmpresaId == empresaId && c.Id == conductorId, ct))
                throw new FlotaNoEncontradaException("Conductor no encontrado en tu empresa.");
            if (!await db.Vehiculos.AnyAsync(v => v.EmpresaId == empresaId && v.Id == vehiculoId, ct))
                throw new FlotaNoEncontradaException("Vehículo no encontrado en tu empresa.");
            if (await db.AutorizacionesVehiculo.AnyAsync(a => a.ConductorId == conductorId && a.VehiculoId == vehiculoId && a.RevocadaEn == null, ct))
                throw new FlotaConflictoException("El vehículo ya está autorizado para ese conductor.");
            var autorizacion = new AutorizacionVehiculo { Id = Guid.NewGuid(), EmpresaId = empresaId, ConductorId = conductorId, VehiculoId = vehiculoId, AutorizadaEn = now, AutorizadaPor = actorId };
            db.AutorizacionesVehiculo.Add(autorizacion);
            return autorizacion.Id;
        }, ct);
    }

    /// <summary>Revocar no cierra un turno ya abierto: solo impide abrir turnos nuevos con ese vehículo.</summary>
    public Task<ResultadoFlota> RevocarAutorizacionAsync(Guid empresaId, Guid actorId, Guid autorizacionId, string clave, CancellationToken ct = default)
    {
        Identidad.Exigir(autorizacionId, nameof(autorizacionId));
        return EjecutarAsync(empresaId, actorId, "revocar_vehiculo", OperacionAdministrar, clave, new { autorizacionId }, "AutorizacionRevocada", async now =>
        {
            var autorizacion = await db.AutorizacionesVehiculo.SingleOrDefaultAsync(a => a.Id == autorizacionId && a.EmpresaId == empresaId, ct)
                ?? throw new FlotaNoEncontradaException("Autorización no encontrada en tu empresa.");
            if (autorizacion.RevocadaEn is not null) throw new FlotaConflictoException("La autorización ya estaba revocada.");
            autorizacion.RevocadaEn = now; autorizacion.RevocadaPor = actorId;
            return autorizacion.Id;
        }, ct);
    }

    public async Task<IReadOnlyList<AutorizacionVista>> AutorizacionesAsync(Guid empresaId, Guid? conductorId, CancellationToken ct = default)
    {
        Identidad.Exigir(empresaId, nameof(empresaId));
        var query = from a in db.AutorizacionesVehiculo.AsNoTracking()
                    join c in db.Conductores.AsNoTracking() on a.ConductorId equals c.Id
                    join v in db.Vehiculos.AsNoTracking() on a.VehiculoId equals v.Id
                    where a.EmpresaId == empresaId && a.RevocadaEn == null
                    select new { a, c.Nombre, v.Patente, Libre = !db.Turnos.Any(t => t.VehiculoId == v.Id && t.Fin == null) };
        if (conductorId is { } id) query = query.Where(x => x.a.ConductorId == id);
        return await query.OrderBy(x => x.Nombre).ThenBy(x => x.Patente)
            .Select(x => new AutorizacionVista(x.a.Id, x.a.ConductorId, x.Nombre, x.a.VehiculoId, x.Patente, x.Libre, x.a.AutorizadaEn))
            .Take(1000).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ConductorVista>> ConductoresAsync(Guid empresaId, CancellationToken ct = default)
    {
        Identidad.Exigir(empresaId, nameof(empresaId));
        var conductores = await db.Conductores.AsNoTracking().Where(c => c.EmpresaId == empresaId)
            .OrderBy(c => c.Nombre).ThenBy(c => c.Id).Take(1000).Select(c => new { c.Id, c.Nombre }).ToListAsync(ct);
        var turnos = await (from t in db.Turnos.AsNoTracking()
                            join v in db.Vehiculos.AsNoTracking() on t.VehiculoId equals v.Id
                            where t.EmpresaId == empresaId && t.Fin == null
                            select new { t.ConductorId, Vista = new TurnoActivoVista(t.Id, v.Id, v.Patente, t.Inicio) })
            .ToDictionaryAsync(x => x.ConductorId, x => x.Vista, ct);
        var ofertas = (await db.Intentos.AsNoTracking().Where(i => i.EmpresaId == empresaId && i.Estado == EstadoIntento.Ofertado)
            .Select(i => i.ConductorId).ToListAsync(ct)).ToHashSet();
        var viajes = (await db.Viajes.AsNoTracking().Where(v => v.EmpresaId == empresaId && Viaje.Activos.Contains(v.Estado))
            .Select(v => v.ConductorId).ToListAsync(ct)).ToHashSet();
        // Acceso activo del conductor (cuenta con rol Conductor vinculada por membresía).
        var accesos = await (from m in db.Membresias.AsNoTracking()
                             join u in db.Users.AsNoTracking() on m.UsuarioId equals u.Id
                             where m.EmpresaId == empresaId && m.ConductorId != null && m.Activa && u.Activo
                             select new { ConductorId = m.ConductorId!.Value, u.Email }).ToListAsync(ct);
        var accesoPorConductor = accesos.GroupBy(x => x.ConductorId).ToDictionary(g => g.Key, g => g.First().Email);
        return conductores.Select(c =>
        {
            var ocupacion = viajes.Contains(c.Id) ? "Viaje" : ofertas.Contains(c.Id) ? "Oferta" : null;
            var turno = turnos.GetValueOrDefault(c.Id);
            var estado = ocupacion is not null ? EstadoConductor.Ocupado : turno is not null ? EstadoConductor.Disponible : EstadoConductor.FueraDeServicio;
            return new ConductorVista(c.Id, c.Nombre, estado, ocupacion, turno, accesoPorConductor.GetValueOrDefault(c.Id));
        }).ToList();
    }

    public async Task<IReadOnlyList<VehiculoVista>> VehiculosAsync(Guid empresaId, CancellationToken ct = default)
    {
        Identidad.Exigir(empresaId, nameof(empresaId));
        return await (from v in db.Vehiculos.AsNoTracking()
                      where v.EmpresaId == empresaId
                      from t in db.Turnos.AsNoTracking().Where(t => t.VehiculoId == v.Id && t.Fin == null).DefaultIfEmpty()
                      from c in db.Conductores.AsNoTracking().Where(c => t != null && c.Id == t.ConductorId).DefaultIfEmpty()
                      orderby v.Patente, v.Id
                      select new VehiculoVista(v.Id, v.Patente, t == null ? null : t.Id, t == null ? null : t.ConductorId, c == null ? null : c.Nombre))
            .Take(1000).ToListAsync(ct);
    }

    public async Task<(int total, IReadOnlyList<TurnoVista> items)> TurnosAsync(Guid empresaId, bool? abiertos, Guid? conductorId,
        int page, int pageSize, CancellationToken ct = default)
    {
        Identidad.Exigir(empresaId, nameof(empresaId));
        var query = from t in db.Turnos.AsNoTracking()
                    join c in db.Conductores.AsNoTracking() on t.ConductorId equals c.Id
                    join v in db.Vehiculos.AsNoTracking() on t.VehiculoId equals v.Id
                    where t.EmpresaId == empresaId
                    select new { t, c.Nombre, v.Patente };
        if (abiertos is true) query = query.Where(x => x.t.Fin == null);
        if (abiertos is false) query = query.Where(x => x.t.Fin != null);
        if (conductorId is { } id) query = query.Where(x => x.t.ConductorId == id);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.t.Inicio).ThenBy(x => x.t.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new TurnoVista(x.t.Id, x.t.ConductorId, x.Nombre, x.t.VehiculoId, x.Patente, x.t.Inicio, x.t.Fin, x.t.MotivoCierre))
            .ToListAsync(ct);
        return (total, items);
    }

    private async Task<ResultadoFlota> EjecutarAsync(Guid empresaId, Guid actorId, string ambito, string operacion, string clave,
        object contenido, string tipoAuditoria, Func<DateTimeOffset, Task<Guid>> accion, CancellationToken ct, Guid? objetivo = null)
    {
        Identidad.Exigir(empresaId, nameof(empresaId)); Identidad.Exigir(actorId, nameof(actorId));
        if (string.IsNullOrWhiteSpace(clave) || clave.Length > 160) throw new ErrorDominio("Idempotency-Key inválida.");
        var huella = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(contenido))));
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Permisos revalidados en la transacción y ANTES de devolver un resultado repetido.
        if (guard is not null) await guard.AuthorizeAsync(empresaId, actorId, operacion, objetivo, ct);
        // Un reintento concurrente con la misma clave espera aquí al COMMIT o ROLLBACK del primero.
        var reclamada = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO operaciones_flota ("EmpresaId", "Ambito", "Clave", "Huella", "RecursoId", "Fecha")
            VALUES ({empresaId}, {ambito}, {clave}, {huella}, {Guid.Empty}, clock_timestamp())
            ON CONFLICT ("EmpresaId", "Ambito", "Clave") DO NOTHING
            """, ct);
        if (reclamada == 0)
        {
            var previa = await db.OperacionesFlota.AsNoTracking()
                .SingleAsync(x => x.EmpresaId == empresaId && x.Ambito == ambito && x.Clave == clave, ct);
            if (previa.Huella != huella) throw new FlotaConflictoException("Idempotency-Key ya utilizada con otro contenido.");
            if (previa.RecursoId == Guid.Empty) throw new InvalidOperationException("Resultado de idempotencia incompleto.");
            await tx.CommitAsync(ct);
            return new(previa.RecursoId, true);
        }
        var now = await AhoraAsync(tx, ct);
        var id = await accion(now);
        var operacionGuardada = await db.OperacionesFlota.SingleAsync(x => x.EmpresaId == empresaId && x.Ambito == ambito && x.Clave == clave, ct);
        operacionGuardada.RecursoId = id;
        operacionGuardada.Fecha = now;
        db.AuditoriaAcceso.Add(new AuditoriaAcceso { EmpresaId = empresaId, UsuarioId = actorId, Fecha = now, Tipo = tipoAuditoria, ObjetoId = id });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(id, false);
    }

    private async Task<DateTimeOffset> AhoraAsync(IDbContextTransaction tx, CancellationToken ct)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = tx.GetDbTransaction();
        command.CommandText = "SELECT clock_timestamp()";
        var time = (DateTime)(await command.ExecuteScalarAsync(ct) ?? throw new InvalidOperationException("Reloj no disponible."));
        return new DateTimeOffset(time.ToUniversalTime());
    }
}
