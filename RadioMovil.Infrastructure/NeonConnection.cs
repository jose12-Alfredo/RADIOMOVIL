using Npgsql;

namespace RadioMovil.Infrastructure;

public static class NeonConnection
{
    public static string Validate(string connectionString, bool requireDirect = false)
    {
        // Neon env pull entrega DATABASE_URL como URI; convertirla al formato Npgsql sin registrar secretos.
        NpgsqlConnectionStringBuilder settings;
        if (connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase) ||
            connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase))
        {
            var uri = new Uri(connectionString);
            var userInfo = uri.UserInfo.Split(':', 2);
            if (userInfo.Length != 2) throw new InvalidOperationException("Credenciales de conexión incompletas.");
            settings = new NpgsqlConnectionStringBuilder
            {
                Host = uri.Host,
                Port = uri.IsDefaultPort ? 5432 : uri.Port,
                Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
                Username = Uri.UnescapeDataString(userInfo[0]),
                Password = Uri.UnescapeDataString(userInfo[1]),
                SslMode = SslMode.VerifyFull
            };
        }
        else settings = new NpgsqlConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.Database) || string.IsNullOrWhiteSpace(settings.Username))
            throw new InvalidOperationException("La conexión Neon debe indicar host, base de datos y usuario.");
        if (settings.SslMode != SslMode.VerifyFull)
            throw new InvalidOperationException("Neon exige Ssl Mode=VerifyFull y certificado confiable.");
        if (requireDirect && settings.Host.Contains("-pooler", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Migraciones y pruebas de sesión requieren conexión directa, no pooler.");
        return settings.ConnectionString;
    }
}
