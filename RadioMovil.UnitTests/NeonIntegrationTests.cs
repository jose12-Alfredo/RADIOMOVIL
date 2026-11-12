using Microsoft.EntityFrameworkCore;
using Npgsql;
using RadioMovil.Domain;
using RadioMovil.Infrastructure;
using Xunit;

namespace RadioMovil.UnitTests;

public sealed class NeonFactAttribute : FactAttribute
{
    public NeonFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("NEON_TESTING_BRANCH") != "testing" ||
            !NeonTestingGuard.IsAuthorized(Environment.GetEnvironmentVariable("NEON_TESTING_DIRECT_CONNECTION_STRING"), requireDirect: true))
            Skip = "Se requiere confirmar la rama testing y una conexión directa al endpoint oficial de Neon Testing.";
    }
}

// Ejecutar exclusivamente en una rama testing clonada de development y con migración ya aplicada.
public sealed class NeonIntegrationTests
{
    [NeonFact]
    public async Task Registro_SaveChangesFallidoRevierteTodaLaTransaccionSinSavepoint()
    {
        var connection = TestingConnection();
        var empresa = Guid.NewGuid(); var central = Guid.NewGuid(); var actor = Guid.NewGuid();
        var key = Guid.NewGuid().ToString("N");
        await using (var setup = Open(connection)) await TestCentrales.CreateAsync(setup, empresa, central);
        await using (var db = Open(connection))
        {
            // Evento huérfano en el mismo SaveChanges: provoca FK violada después de
            // reclamar idempotencia y contador. Debe abortarse la transacción completa.
            db.Eventos.Add(new SolicitudEvento(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, actor, "Prueba", "FK"));
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => new RegistroSolicitudes(db)
                .RegistrarAsync(empresa, central, actor, "Terminal", key, DateTimeOffset.UtcNow));
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
            Assert.True(db.Database.AutoSavepointsEnabled);
        }
        await using var verify = Open(connection);
        Assert.False(await verify.Solicitudes.AnyAsync(x => x.EmpresaId == empresa));
        Assert.False(await verify.Contadores.AnyAsync(x => x.EmpresaId == empresa && x.CentralId == central));
        Assert.False(await verify.Idempotencias.AnyAsync(x => x.EmpresaId == empresa && x.Clave == key));
    }

    private const string InitialMigration = "20261105170844_InicialPersistencia";
    private static string TestingConnection() =>
        NeonTestingGuard.RequireTesting(Environment.GetEnvironmentVariable("NEON_TESTING_DIRECT_CONNECTION_STRING"), requireDirect: true);

    private static RadioMovilDbContext Open(string connection) => new(new DbContextOptionsBuilder<RadioMovilDbContext>()
        .UseNpgsql(connection, x => x.UseNetTopologySuite()).Options);

    private static async Task<(Guid empresa, Guid central, Guid actor, Guid conductor, Guid solicitud)> SeedAsync(string connection)
    {
        var empresa = Guid.NewGuid(); var central = Guid.NewGuid(); var actor = Guid.NewGuid();
        await using var db = Open(connection);
        var conductor = new Conductor(empresa, "Conductor prueba");
        db.Conductores.Add(conductor);
        await db.SaveChangesAsync();
        await AbrirTurnoAsync(db, empresa, conductor.Id);
        await TestCentrales.CreateAsync(db, empresa, central);
        var (solicitud, _) = await new RegistroSolicitudes(db).RegistrarAsync(empresa, central, actor, "Origen", Guid.NewGuid().ToString(), DateTimeOffset.UtcNow);
        return (empresa, central, actor, conductor.Id, solicitud);
    }

    // Desde la Fase B la base exige turno abierto para insertar una oferta.
    private static async Task AbrirTurnoAsync(RadioMovilDbContext db, Guid empresa, Guid conductor)
    {
        var vehiculo = new Vehiculo(empresa, "IT" + Guid.NewGuid().ToString("N")[..10]);
        db.Vehiculos.Add(vehiculo);
        await db.SaveChangesAsync();
        db.Turnos.Add(new Turno(empresa, conductor, vehiculo.Id, DateTimeOffset.UtcNow.AddMinutes(-1)));
        await db.SaveChangesAsync();
    }

    private static async Task<Solicitud> LoadAsync(RadioMovilDbContext db, Guid id) =>
        await db.Solicitudes.Include(x => x.Intentos).Include(x => x.Eventos).SingleAsync(x => x.Id == id);

    private static void ExpectPg(DbUpdateException error, string code) => Assert.Equal(code, Assert.IsType<PostgresException>(error.InnerException).SqlState);

    [NeonFact]
    public async Task Registro_PersisteHistoriaYReintentosTrasReiniciarContexto()
    {
        var connection = TestingConnection(); var empresa = Guid.NewGuid(); var central = Guid.NewGuid(); var actor = Guid.NewGuid(); var key = Guid.NewGuid().ToString();
        await using (var setup = Open(connection)) await TestCentrales.CreateAsync(setup, empresa, central);
        Guid id; long order;
        await using (var db = Open(connection)) (id, order) = await new RegistroSolicitudes(db).RegistrarAsync(empresa, central, actor, "Terminal", key, DateTimeOffset.UtcNow);
        await using (var db = Open(connection))
        {
            var (sameId, sameOrder) = await new RegistroSolicitudes(db).RegistrarAsync(empresa, central, actor, "Terminal", key, DateTimeOffset.UtcNow);
            Assert.Equal((id, order), (sameId, sameOrder));
            var restored = await LoadAsync(db, id);
            Assert.Equal(empresa, restored.EmpresaId);
            Assert.Single(restored.Eventos);
            Assert.Equal(order, db.Entry(restored).Property<long>("OrdenRecepcion").CurrentValue);
        }
    }

    [NeonFact]
    public async Task Registro_Concurrente_ProduceOrdenUnicoPorCentral()
    {
        var connection = TestingConnection(); var empresa = Guid.NewGuid(); var central = Guid.NewGuid(); var actor = Guid.NewGuid();
        await using (var setup = Open(connection)) await TestCentrales.CreateAsync(setup, empresa, central);
        async Task<(Guid solicitudId, long orden)> RegisterAsync()
        {
            await using var db = Open(connection);
            return await new RegistroSolicitudes(db).RegistrarAsync(empresa, central, actor, "Terminal", Guid.NewGuid().ToString(), DateTimeOffset.UtcNow);
        }
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => RegisterAsync()));
        Assert.Equal(Enumerable.Range(1, 8).Select(x => (long)x), results.Select(x => x.orden).Order());
        Assert.Equal(8, results.Select(x => x.solicitudId).Distinct().Count());
    }

    [NeonFact]
    public async Task ClaveForaneaYEmpresa_AjenaRechazaOferta()
    {
        var connection = TestingConnection(); var (empresa, _, actor, conductor, _) = await SeedAsync(connection);
        var otra = await SeedAsync(connection);
        await using var db = Open(connection);
        var solicitud = await LoadAsync(db, otra.solicitud);
        solicitud.Ofrecer(conductor, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(1), actor);
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        // El trigger (turno abierto en la MISMA empresa) rechaza antes que la FK compuesta.
        Assert.Contains(Assert.IsType<PostgresException>(error.InnerException).SqlState,
            new[] { PostgresErrorCodes.ForeignKeyViolation, PostgresErrorCodes.CheckViolation });
    }

    [NeonFact]
    public async Task OfertaDuplicadaMismaSolicitud_IndiceUnico()
    {
        var connection = TestingConnection(); var seed = await SeedAsync(connection);
        await using var db = Open(connection);
        var other = new Conductor(seed.empresa, "Segundo"); db.Conductores.Add(other); await db.SaveChangesAsync();
        await AbrirTurnoAsync(db, seed.empresa, other.Id);
        var solicitud = await LoadAsync(db, seed.solicitud);
        solicitud.Ofrecer(seed.conductor, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(1), seed.actor);
        await db.SaveChangesAsync();
        await using var second = Open(connection);
        // SQL parametrizado evita la validación local para probar el índice real.
        var error = await Assert.ThrowsAsync<PostgresException>(() => second.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO intentos_despacho ("Id","EmpresaId","SolicitudId","ConductorId","CreadoEn","VenceEn","Estado")
            VALUES ({Guid.NewGuid()},{seed.empresa},{seed.solicitud},{other.Id},{DateTimeOffset.UtcNow},{DateTimeOffset.UtcNow.AddMinutes(1)},{"Ofertado"})
            """));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
    }

    [NeonFact]
    public async Task DosOperadores_CompitiendoPorConductor_UnoPierde()
    {
        var connection = TestingConnection(); var seed = await SeedAsync(connection);
        await using var prepare = Open(connection);
        var (secondId, _) = await new RegistroSolicitudes(prepare).RegistrarAsync(seed.empresa, seed.central, seed.actor, "Otro", Guid.NewGuid().ToString(), DateTimeOffset.UtcNow);
        async Task<Exception?> OfferAsync(Guid id)
        {
            await using var db = Open(connection);
            var solicitud = await LoadAsync(db, id);
            solicitud.Ofrecer(seed.conductor, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(1), seed.actor);
            try { await db.SaveChangesAsync(); return null; } catch (DbUpdateException exception) { return exception; }
        }
        var results = await Task.WhenAll(OfferAsync(seed.solicitud), OfferAsync(secondId));
        Assert.Single(results, x => x is null);
        ExpectPg(Assert.IsType<DbUpdateException>(Assert.Single(results, x => x is not null)), PostgresErrorCodes.UniqueViolation);
    }

    [NeonFact]
    public async Task OfertaYViaje_ExclusividadCruzada()
    {
        var connection = TestingConnection(); var seed = await SeedAsync(connection);
        await using var db = Open(connection);
        var solicitud = await LoadAsync(db, seed.solicitud);
        solicitud.Ofrecer(seed.conductor, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(1), seed.actor);
        await db.SaveChangesAsync();
        var (secondId, _) = await new RegistroSolicitudes(db).RegistrarAsync(seed.empresa, seed.central, seed.actor, "Otro", Guid.NewGuid().ToString(), DateTimeOffset.UtcNow);
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO viajes ("Id","EmpresaId","SolicitudId","ConductorId","AsignadoEn","Estado")
            VALUES ({Guid.NewGuid()},{seed.empresa},{secondId},{seed.conductor},{DateTimeOffset.UtcNow},{"Asignado"})
            """));
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
    }

    [NeonFact]
    public async Task ViajeYOferta_ExclusividadInversa()
    {
        var connection = TestingConnection(); var seed = await SeedAsync(connection);
        await using var db = Open(connection);
        var (secondId, _) = await new RegistroSolicitudes(db).RegistrarAsync(seed.empresa, seed.central, seed.actor, "Otro", Guid.NewGuid().ToString(), DateTimeOffset.UtcNow);
        db.Viajes.Add(new Viaje(seed.empresa, seed.solicitud, seed.conductor, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO intentos_despacho ("Id","EmpresaId","SolicitudId","ConductorId","CreadoEn","VenceEn","Estado")
            VALUES ({Guid.NewGuid()},{seed.empresa},{secondId},{seed.conductor},{DateTimeOffset.UtcNow},{DateTimeOffset.UtcNow.AddMinutes(1)},{"Ofertado"})
            """));
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
    }

    [NeonFact]
    public async Task Idempotencia_ConcurrenteMismaClave_DevuelveMismoIdYOrden()
    {
        var connection = TestingConnection(); var empresa = Guid.NewGuid(); var central = Guid.NewGuid(); var actor = Guid.NewGuid(); var key = Guid.NewGuid().ToString();
        await using (var setup = Open(connection)) await TestCentrales.CreateAsync(setup, empresa, central);
        async Task<(Guid solicitudId, long orden)> RegisterAsync()
        {
            await using var db = Open(connection);
            return await new RegistroSolicitudes(db).RegistrarAsync(empresa, central, actor, "Terminal", key, DateTimeOffset.UtcNow);
        }
        var values = await Task.WhenAll(RegisterAsync(), RegisterAsync());
        Assert.Equal(values[0], values[1]);
        await using var check = Open(connection);
        Assert.Equal(1, await check.Solicitudes.CountAsync(x => x.EmpresaId == empresa));
    }

    [NeonFact]
    public async Task MigracionDesdeEsquemaVacio_EnEsquemaTemporalDeTesting()
    {
        var connection = TestingConnection();
        var schema = "rm_test_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(connection);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", admin)) await create.ExecuteNonQueryAsync();
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connection) { SearchPath = $"{schema},public" };
            await using var db = new RadioMovilDbContext(new DbContextOptionsBuilder<RadioMovilDbContext>()
                .UseNpgsql(builder.ConnectionString, x => x.UseNetTopologySuite().MigrationsHistoryTable("__EFMigrationsHistory", schema)).Options);
            await db.Database.OpenConnectionAsync();
            // Identificador generado exclusivamente a partir de Guid("N"), nunca entrada de usuario.
            await using (var setPath = db.Database.GetDbConnection().CreateCommand())
            {
                setPath.CommandText = $"SET search_path TO \"{schema}\", public";
                await setPath.ExecuteNonQueryAsync();
            }
            await using (var current = db.Database.GetDbConnection().CreateCommand())
            {
                current.CommandText = "SELECT current_schema()";
                Assert.Equal(schema, await current.ExecuteScalarAsync());
            }
            await db.Database.MigrateAsync();
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            await using var cmd = new NpgsqlCommand("SELECT to_regclass(@relation) IS NOT NULL", admin);
            cmd.Parameters.AddWithValue("relation", $"{schema}.solicitudes");
            Assert.True((bool)(await cmd.ExecuteScalarAsync())!);
        }
        finally
        {
            // Solo elimina el esquema efímero creado por esta prueba en la rama testing aislada.
            await using var cleanup = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", admin);
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    // Schema-only de Neon no copia las filas de __EFMigrationsHistory. Se registra la
    // migración heredada SOLO después de comprobar tablas, índices, FKs y PostGIS.
    [NeonFact]
    public async Task BaselineSchemaOnly_ValidaEInscribeHistorialEnTesting()
    {
        await using var db = Open(TestingConnection());
        var expected = new[] { "solicitudes", "conductores", "vehiculos", "turnos", "intentos_despacho", "viajes", "solicitud_eventos", "contadores_central", "claves_idempotencia", "outbox", "ubicaciones_conductor", "__EFMigrationsHistory" };
        await db.Database.OpenConnectionAsync();
        await using var cmd = db.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM pg_extension WHERE extname='postgis'";
        Assert.Equal(1L, (long)(await cmd.ExecuteScalarAsync())!);
        foreach (var name in expected)
        {
            cmd.CommandText = "SELECT to_regclass(@relation) IS NOT NULL";
            cmd.Parameters.Clear();
            var parameter = cmd.CreateParameter(); parameter.ParameterName = "relation"; parameter.Value = "public.\"" + name + "\""; cmd.Parameters.Add(parameter);
            Assert.True((bool)(await cmd.ExecuteScalarAsync())!, $"Falta {name}");
        }
        cmd.Parameters.Clear();
        cmd.CommandText = "SELECT count(*) FROM pg_constraint WHERE contype='f' AND conrelid IN ('intentos_despacho'::regclass,'viajes'::regclass,'turnos'::regclass)";
        Assert.True((long)(await cmd.ExecuteScalarAsync())! >= 6);
        cmd.CommandText = "SELECT count(*) FROM pg_indexes WHERE tablename IN ('intentos_despacho','viajes') AND indexdef LIKE '%WHERE%'";
        Assert.True((long)(await cmd.ExecuteScalarAsync())! >= 4);
        cmd.CommandText = "INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES (@migration, @version) ON CONFLICT (\"MigrationId\") DO NOTHING";
        cmd.Parameters.Clear();
        var migration = cmd.CreateParameter(); migration.ParameterName = "migration"; migration.Value = InitialMigration; cmd.Parameters.Add(migration);
        var version = cmd.CreateParameter(); version.ParameterName = "version"; version.Value = "10.0.4"; cmd.Parameters.Add(version);
        await cmd.ExecuteNonQueryAsync();
    }

    [NeonFact]
    public async Task MigracionYPostgis_ExistenEnRamaTesting()
    {
        await using var db = Open(TestingConnection());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        await using var command = db.Database.GetDbConnection().CreateCommand();
        await db.Database.OpenConnectionAsync();
        command.CommandText = "SELECT EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'postgis')";
        Assert.True((bool)(await command.ExecuteScalarAsync())!);
    }

    [NeonFact]
    public async Task Transaccion_ReversionNoPublicaDatos()
    {
        var connection = TestingConnection();
        var empresa = Guid.NewGuid();
        await using var db = Open(connection);
        // Aislamiento: la prueba revierte todos sus cambios.
        await using var tx = await db.Database.BeginTransactionAsync();
        var conductor = new Conductor(empresa, "Prueba");
        db.Conductores.Add(conductor);
        await db.SaveChangesAsync();
        await using var second = Open(connection);
        Assert.False(await second.Conductores.AnyAsync(x => x.Id == conductor.Id));
        await tx.RollbackAsync();
        Assert.False(await db.Conductores.AsNoTracking().AnyAsync(x => x.Id == conductor.Id));
    }
}
