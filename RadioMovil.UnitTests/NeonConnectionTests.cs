using RadioMovil.Infrastructure;
using Npgsql;
using Xunit;

namespace RadioMovil.UnitTests;

public sealed class NeonConnectionTests
{
    [Fact]
    public void RechazaConexionSinVerificacionTls()
    {
        Assert.Throws<InvalidOperationException>(() => NeonConnection.Validate("Host=example.neon.tech;Database=test;Username=app;Password=dummy;Ssl Mode=Require"));
    }

    [Fact]
    public void RechazaPoolerParaMigraciones()
    {
        Assert.Throws<InvalidOperationException>(() => NeonConnection.Validate("Host=example-pooler.neon.tech;Database=test;Username=app;Password=dummy;Ssl Mode=VerifyFull", requireDirect: true));
    }

    [Fact]
    public void AdmiteUriNeonYExigeVerificacionCompleta()
    {
        var result = NeonConnection.Validate("postgresql://app:dummy@example.neon.tech/demo?sslmode=require");
        var parsed = new NpgsqlConnectionStringBuilder(result);
        Assert.Equal("example.neon.tech", parsed.Host);
        Assert.Equal(SslMode.VerifyFull, parsed.SslMode);
    }
}
