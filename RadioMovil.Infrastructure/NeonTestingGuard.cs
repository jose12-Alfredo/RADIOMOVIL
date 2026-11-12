using Npgsql;

namespace RadioMovil.Infrastructure;

/// <summary>
/// Lista de permitidos del host Testing. Es una constante versionada a propósito: una variable
/// mal etiquetada no puede ampliarla, y no hace falta tener credenciales de otras ramas para comparar.
/// Verificado con la API de Neon: proyecto RADIO MOVIL, rama testing (br-morning-pine-bdzyesoo).
/// Si el endpoint se recrea, actualizar este valor tras verificarlo de nuevo.
/// </summary>
public static class NeonTestingGuard
{
    private const string DirectHost = "ep-shy-math-bdinnn5q.c-8.us-east-2.aws.neon.tech";
    private const string PooledHost = "ep-shy-math-bdinnn5q-pooler.c-8.us-east-2.aws.neon.tech";

    public static bool IsAuthorized(string? connectionString, bool requireDirect = false)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return false;
        try
        {
            var settings = new NpgsqlConnectionStringBuilder(NeonConnection.Validate(connectionString, requireDirect));
            return IsTestingHost(settings.Host) || IsUnreachableProbe(settings);
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or FormatException)
        {
            return false;
        }
    }

    /// <summary>Devuelve la conexión normalizada o lanza sin incluir datos de la conexión en el mensaje.</summary>
    public static string RequireTesting(string? connectionString, bool requireDirect = false)
    {
        if (Environment.GetEnvironmentVariable("NEON_TESTING_BRANCH") != "testing")
            throw new InvalidOperationException("La rama activa no está confirmada como testing.");
        if (!IsAuthorized(connectionString, requireDirect))
            throw new InvalidOperationException("Conexión rechazada: no corresponde al endpoint oficial de Neon Testing.");
        return NeonConnection.Validate(connectionString!, requireDirect);
    }

    private static bool IsTestingHost(string? host) =>
        string.Equals(host, DirectHost, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(host, PooledHost, StringComparison.OrdinalIgnoreCase);

    // Destinos que nunca alcanzan una base de datos: dominio reservado .invalid (RFC 2606) y el
    // puerto 1 local que usan las pruebas de base de datos no disponible.
    private static bool IsUnreachableProbe(NpgsqlConnectionStringBuilder settings) =>
        settings.Host is { } host &&
        (host.EndsWith(".invalid", StringComparison.OrdinalIgnoreCase) && !host.Contains(',') ||
         host == "127.0.0.1" && settings.Port == 1);
}
