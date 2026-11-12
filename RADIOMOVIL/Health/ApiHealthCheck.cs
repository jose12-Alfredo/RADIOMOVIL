using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace RADIOMOVIL.Health;

public sealed class ApiHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(HealthCheckResult.Healthy("La API responde."));
    }
}
