using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RadioMovil.Infrastructure;

namespace RADIOMOVIL.Health;

public sealed class DatabaseHealthCheck(IServiceScopeFactory scopeFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var database = scope.ServiceProvider.GetService<RadioMovilDbContext>();
        if (database is null)
            return HealthCheckResult.Unhealthy("La conexión a PostgreSQL no está configurada.");

        try
        {
            // Solo lectura: comprueba conexión y que exista la tabla creada por la migración.
            await database.Solicitudes.AsNoTracking().Take(1).AnyAsync(cancellationToken);
            return HealthCheckResult.Healthy("PostgreSQL responde y la tabla solicitudes existe.");
        }
        catch (Exception exception) when (exception is DbException or InvalidOperationException or TimeoutException)
        {
            // No incluir errores del proveedor: pueden contener detalles de conexión.
            return HealthCheckResult.Unhealthy("No se pudo consultar PostgreSQL.");
        }
    }
}
