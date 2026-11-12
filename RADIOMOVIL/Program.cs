using RADIOMOVIL.Health;
using RadioMovil.Infrastructure;
using RadioMovil.Workers;
using RADIOMOVIL.Security;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace RADIOMOVIL
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.

            builder.Services.AddControllers();
            // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
            builder.Services.AddOpenApi();
            builder.Services.AddProblemDetails();
            builder.Services.AddHealthChecks().AddCheck<ApiHealthCheck>("api");
            builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");
            // Solo secretos de usuario o variables de entorno; no se cargan credenciales desde archivos del proyecto.
            var connectionString = builder.Environment.EnvironmentName switch
            {
                "Development" => Environment.GetEnvironmentVariable("NEON_DEVELOPMENT_CONNECTION_STRING")
                    ?? Environment.GetEnvironmentVariable("NEON_DEVELOPMENT_DIRECT_CONNECTION_STRING"),
                "Testing" => Environment.GetEnvironmentVariable("NEON_TESTING_CONNECTION_STRING")
                    ?? Environment.GetEnvironmentVariable("NEON_TESTING_DIRECT_CONNECTION_STRING"),
                "Production" => Environment.GetEnvironmentVariable("NEON_PRODUCTION_CONNECTION_STRING")
                    ?? Environment.GetEnvironmentVariable("DATABASE_URL"),
                _ => throw new InvalidOperationException("Entorno no autorizado para persistencia.")
            };
            // Antes de registrar persistencia: Testing solo puede usar el endpoint oficial de su rama.
            if (builder.Environment.IsEnvironment("Testing") && !string.IsNullOrWhiteSpace(connectionString))
                NeonTestingGuard.RequireTesting(connectionString);
            if (!string.IsNullOrWhiteSpace(connectionString))
                builder.Services.AddPersistencia(connectionString, builder.Configuration.GetSection("Gps").Get<GpsOptions>());
            // Rutas del conductor: OSRM PROPIO por configuración; sin redirecciones ni URL tomada de la petición.
            builder.Services.AddSingleton(builder.Configuration.GetSection("Rutas").Get<RADIOMOVIL.Rutas.RutasOptions>() ?? new RADIOMOVIL.Rutas.RutasOptions());
            builder.Services.AddHttpClient<RADIOMOVIL.Rutas.ServicioRutas>()
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
            if (builder.Environment.IsEnvironment("Testing"))
            {
                if (Environment.GetEnvironmentVariable("NEON_TESTING_BRANCH") != "testing" || string.IsNullOrWhiteSpace(connectionString))
                    throw new InvalidOperationException("La API de solicitudes requiere la rama Testing configurada.");
                builder.Services.AddEnterpriseSecurity();
                // Sin autenticación empresarial, los workers solo se activan explícitamente
                // en el host Testing. Ninguna ruta de negocio se habilita por esto.
                if (Environment.GetEnvironmentVariable("RADIOMOVIL_WORKERS_ENABLED") == "1")
                {
                    if (!Guid.TryParse(Environment.GetEnvironmentVariable("RADIOMOVIL_WORKER_ACTOR_ID"), out var workerId) || workerId == Guid.Empty)
                        throw new InvalidOperationException("Actor de Workers Testing obligatorio.");
                    var batch = int.TryParse(Environment.GetEnvironmentVariable("RADIOMOVIL_WORKERS_BATCH_SIZE"), out var size) ? size : 16;
                    var interval = int.TryParse(Environment.GetEnvironmentVariable("RADIOMOVIL_WORKERS_INTERVAL_SECONDS"), out var seconds) ? seconds : 5;
                    builder.Services.AddRadioMovilWorkers(new WorkerOptions { ActorId = workerId, BatchSize = batch, Interval = TimeSpan.FromSeconds(interval) });
                }
            }

            var app = builder.Build();
            if (app.Environment.IsEnvironment("Testing"))
                await BootstrapAdmin.RunIfRequestedAsync(app.Services, CancellationToken.None);
            app.UseExceptionHandler();

            // Configure the HTTP request pipeline.
            // Testing publica el contrato para la app Flutter: /openapi/v1.json (solo documento, sin datos).
            if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
            {
                app.MapOpenApi();
            }

            app.UseHttpsRedirection();

            if (app.Environment.IsEnvironment("Testing"))
            {
                app.UseAuthentication();
                app.Use(EnterpriseSecurity.EnforceSessionAndCsrfAsync);
                app.UseAuthorization();
            }


            // Migración y autenticación empresarial verificadas solo en Testing;
            // no publicar rutas en Development/Production sin despliegue autorizado.
            if (app.Environment.IsEnvironment("Testing")) app.MapControllers();
            app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = check => check.Name == "api" });
            app.MapHealthChecks("/health/db", new HealthCheckOptions { Predicate = check => check.Name == "database" });

            await app.RunAsync();
        }
    }
}
