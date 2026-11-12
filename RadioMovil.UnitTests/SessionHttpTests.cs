using System.Data.Common;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using RadioMovil.Infrastructure;
using RADIOMOVIL;
using Xunit;

namespace RadioMovil.UnitTests;

public sealed class SessionHttpTests
{
    [Fact]
    public async Task Me_SinSesion_NoPermiteSeleccionarIdentidadNiConsultaPersistencia()
    {
        // Solo TestServer en memoria; ninguna conexión de entorno ni servidor externo.
        var variables = new Dictionary<string, string?>
        {
            ["NEON_TESTING_BRANCH"] = "testing",
            ["NEON_TESTING_CONNECTION_STRING"] = "Host=offline.invalid;Database=offline;Username=offline;Ssl Mode=VerifyFull",
            ["RADIOMOVIL_BOOTSTRAP_ADMIN"] = null,
            ["RADIOMOVIL_WORKERS_ENABLED"] = null
        };
        var previous = variables.ToDictionary(x => x.Key, x => Environment.GetEnvironmentVariable(x.Key));
        try
        {
            foreach (var item in variables) Environment.SetEnvironmentVariable(item.Key, item.Value);
            using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
                builder.UseEnvironment("Testing").ConfigureServices(services =>
                    services.AddDbContext<RadioMovilDbContext>(options => options.AddInterceptors(new RejectConnections()))));
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
                { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
            foreach (var path in new[] { "/api/v1/auth/me",
                $"/api/v1/auth/me?empresaId={Guid.NewGuid()}&usuarioId={Guid.NewGuid()}" })
            {
                using var response = await client.GetAsync(path);
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
                Assert.Null(response.Headers.Location);
                Assert.Equal("", await response.Content.ReadAsStringAsync());
            }
        }
        finally
        {
            foreach (var item in previous) Environment.SetEnvironmentVariable(item.Key, item.Value);
        }
    }

    private sealed class RejectConnections : DbConnectionInterceptor
    {
        public override InterceptionResult ConnectionOpening(DbConnection connection,
            ConnectionEventData eventData, InterceptionResult result) =>
            throw new InvalidOperationException("La prueba offline no permite abrir conexiones.");

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection,
            ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("La prueba offline no permite abrir conexiones.");
    }
}
