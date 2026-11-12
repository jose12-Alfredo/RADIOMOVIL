using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using RadioMovil.Infrastructure;
using RADIOMOVIL;
using Xunit;

namespace RadioMovil.UnitTests;

// Sin conexiones: solo se analiza el destino. Las contraseñas son marcadores, no credenciales.
public sealed class NeonTestingGuardTests
{
    private const string Testing = "ep-shy-math-bdinnn5q.c-8.us-east-2.aws.neon.tech";
    private const string Pooled = "ep-shy-math-bdinnn5q-pooler.c-8.us-east-2.aws.neon.tech";
    private const string Production = "ep-purple-forest-bd6z9mxm.c-8.us-east-2.aws.neon.tech";
    private const string Development = "ep-falling-waterfall-bdv2rw56.c-8.us-east-2.aws.neon.tech";

    private static string Npgsql(string host, string ssl = "VerifyFull") =>
        $"Host={host};Database=neondb;Username=app;Password=marcador;Ssl Mode={ssl}";

    [Theory]
    [InlineData(Testing)]
    [InlineData(Pooled)]
    [InlineData("EP-SHY-MATH-BDINNN5Q.C-8.US-EAST-2.AWS.NEON.TECH")]
    [InlineData("offline.invalid")]
    public void Admite_EndpointOficialYDestinosInalcanzables(string host) =>
        Assert.True(NeonTestingGuard.IsAuthorized(Npgsql(host)));

    [Fact]
    public void Admite_UriDelEndpointOficial() =>
        Assert.True(NeonTestingGuard.IsAuthorized($"postgresql://app:marcador@{Testing}/neondb?sslmode=verify-full", requireDirect: true));

    [Theory]
    [InlineData(Production)]
    [InlineData(Development)]
    [InlineData("ep-purple-forest-bd6z9mxm-pooler.c-8.us-east-2.aws.neon.tech")]
    [InlineData("ep-falling-waterfall-bdv2rw56-pooler.c-8.us-east-2.aws.neon.tech")]
    [InlineData(Testing + ".example.com")]
    [InlineData("x" + Testing)]
    [InlineData(Testing + "," + Production)]
    [InlineData(Production + ",offline.invalid")]
    [InlineData("localhost")]
    public void Rechaza_CualquierOtroDestino(string host) =>
        Assert.False(NeonTestingGuard.IsAuthorized(Npgsql(host)));

    [Fact]
    public void Rechaza_UriDeProduction() =>
        Assert.False(NeonTestingGuard.IsAuthorized($"postgresql://app:marcador@{Production}/neondb?sslmode=verify-full"));

    [Fact]
    public void Rechaza_PoolerCuandoSeExigeDirecta() =>
        Assert.False(NeonTestingGuard.IsAuthorized(Npgsql(Pooled), requireDirect: true));

    [Fact]
    public void Rechaza_SinVerificacionTlsCompleta() =>
        Assert.False(NeonTestingGuard.IsAuthorized(Npgsql(Testing, "Require")));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no es una conexion")]
    public void Rechaza_ConexionAusenteOInvalida(string? connection) =>
        Assert.False(NeonTestingGuard.IsAuthorized(connection));

    [Fact]
    public void Rechaza_LoopbackFueraDelPuertoDePrueba()
    {
        Assert.True(NeonTestingGuard.IsAuthorized("Host=127.0.0.1;Port=1;Database=d;Username=u;SSL Mode=VerifyFull"));
        Assert.False(NeonTestingGuard.IsAuthorized("Host=127.0.0.1;Port=5432;Database=d;Username=u;SSL Mode=VerifyFull"));
    }

    [Fact]
    public void MensajeDeRechazo_NoExponeDatosDeLaConexion()
    {
        var previous = Environment.GetEnvironmentVariable("NEON_TESTING_BRANCH");
        Environment.SetEnvironmentVariable("NEON_TESTING_BRANCH", "testing");
        try
        {
            var error = Assert.Throws<InvalidOperationException>(() => NeonTestingGuard.RequireTesting(Npgsql(Production)));
            Assert.DoesNotContain("marcador", error.Message);
            Assert.DoesNotContain(Production, error.Message);
        }
        finally { Environment.SetEnvironmentVariable("NEON_TESTING_BRANCH", previous); }
    }

    [Fact]
    public void HostTesting_NoArrancaConLaConexionDeProduction()
    {
        var variables = new Dictionary<string, string?>
        {
            ["NEON_TESTING_BRANCH"] = "testing",
            ["NEON_TESTING_CONNECTION_STRING"] = Npgsql(Production),
            ["RADIOMOVIL_BOOTSTRAP_ADMIN"] = null,
            ["RADIOMOVIL_WORKERS_ENABLED"] = null
        };
        var previous = variables.ToDictionary(x => x.Key, x => Environment.GetEnvironmentVariable(x.Key));
        try
        {
            foreach (var item in variables) Environment.SetEnvironmentVariable(item.Key, item.Value);
            using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(x => x.UseEnvironment("Testing"));
            var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
            Assert.Contains("endpoint oficial de Neon Testing", error.Message);
        }
        finally
        {
            foreach (var item in previous) Environment.SetEnvironmentVariable(item.Key, item.Value);
        }
    }
}
