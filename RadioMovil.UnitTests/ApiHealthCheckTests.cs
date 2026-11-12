using Microsoft.Extensions.Diagnostics.HealthChecks;
using RADIOMOVIL.Health;
using Xunit;

namespace RadioMovil.UnitTests;

public sealed class ApiHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_WhenApiIsRunning_ReturnsHealthy()
    {
        var healthCheck = new ApiHealthCheck();

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal("La API responde.", result.Description);
    }
}
