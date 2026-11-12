using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RadioMovil.Infrastructure;

public sealed class RadioMovilDesignTimeFactory : IDesignTimeDbContextFactory<RadioMovilDbContext>
{
    public RadioMovilDbContext CreateDbContext(string[] args)
    {
        var testing = Environment.GetEnvironmentVariable("RADIOMOVIL_MIGRATION_TARGET") == "testing";
        if (testing && Environment.GetEnvironmentVariable("NEON_TESTING_BRANCH") != "testing")
            throw new InvalidOperationException("Migración Testing requiere confirmación de rama.");
        var connectionString = testing
            ? Environment.GetEnvironmentVariable("NEON_TESTING_DIRECT_CONNECTION_STRING")
            : Environment.GetEnvironmentVariable("NEON_DEVELOPMENT_DIRECT_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Se requiere conexión directa explícita para el destino de migración.");
        var validated = testing
            ? NeonTestingGuard.RequireTesting(connectionString, requireDirect: true)
            : NeonConnection.Validate(connectionString, requireDirect: true);
        var options = new DbContextOptionsBuilder<RadioMovilDbContext>()
            .UseNpgsql(validated, npgsql => npgsql.UseNetTopologySuite()).Options;
        return new RadioMovilDbContext(options);
    }
}
