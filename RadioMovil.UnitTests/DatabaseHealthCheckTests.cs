using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RADIOMOVIL.Health;
using Xunit;

namespace RadioMovil.UnitTests;

public sealed class DatabaseHealthCheckTests
{
    [Fact]
    public async Task SinConexionConfigurada_DevuelveUnhealthy()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var check = new DatabaseHealthCheck(services.GetRequiredService<IServiceScopeFactory>());

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }
}
