using System.Globalization;
using System.Text.Json;

namespace RADIOMOVIL.Rutas;

/// <summary>
/// Servidor de rutas PROPIO (decisión del responsable 2026-11-10): OSRM autoalojado. La URL base viene SOLO de la
/// configuración (`Rutas:OsrmUrl`), nunca de la petición; sin configurar, la API responde 503 y la app muestra solo los puntos.
/// Prohibido apuntarla al servidor demo público de OSRM o a un proveedor de pago sin autorización.
/// </summary>
public sealed class RutasOptions
{
    public string? OsrmUrl { get; set; }
    public int TimeoutSegundos { get; set; } = 5;
}

public sealed record RutaCalculada(double DistanciaMetros, double DuracionSegundos, IReadOnlyList<double[]> Coordenadas);
public sealed class RutaNoDisponibleException(string message) : Exception(message);

public sealed class ServicioRutas(HttpClient http, RutasOptions options)
{
    public bool Configurado => Base is not null;

    private Uri? Base => Uri.TryCreate(options.OsrmUrl, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.Query)
        ? new Uri(uri.AbsoluteUri.TrimEnd('/') + "/") : null;

    public async Task<RutaCalculada> CalcularAsync(double desdeLat, double desdeLng, double hastaLat, double hastaLng, CancellationToken ct)
    {
        var baseUri = Base ?? throw new RutaNoDisponibleException("Servicio de rutas no configurado.");
        string F(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);
        var url = new Uri(baseUri, $"route/v1/driving/{F(desdeLng)},{F(desdeLat)};{F(hastaLng)},{F(hastaLat)}?overview=full&geometries=geojson&alternatives=false&steps=false");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSegundos, 1, 30)));
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode) throw new RutaNoDisponibleException($"El servicio de rutas respondió {(int)response.StatusCode}.");
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token);
            var root = json.RootElement;
            if (root.GetProperty("code").GetString() != "Ok" || root.GetProperty("routes").GetArrayLength() == 0)
                throw new RutaNoDisponibleException("No se encontró una ruta entre los dos puntos.");
            var route = root.GetProperty("routes")[0];
            var coordinates = route.GetProperty("geometry").GetProperty("coordinates").EnumerateArray()
                .Select(p => new[] { Math.Round(p[0].GetDouble(), 6), Math.Round(p[1].GetDouble(), 6) }).ToList();
            if (coordinates.Count < 2) throw new RutaNoDisponibleException("Ruta sin geometría.");
            return new(Math.Round(route.GetProperty("distance").GetDouble()), Math.Round(route.GetProperty("duration").GetDouble()), coordinates);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new RutaNoDisponibleException("El servicio de rutas no respondió a tiempo."); }
        catch (HttpRequestException) { throw new RutaNoDisponibleException("El servicio de rutas no está disponible."); }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        { throw new RutaNoDisponibleException("Respuesta inválida del servicio de rutas."); }
    }
}
