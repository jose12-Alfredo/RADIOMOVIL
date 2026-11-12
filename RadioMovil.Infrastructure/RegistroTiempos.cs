using System.Diagnostics;

namespace RadioMovil.Infrastructure;

// Scoped; only timings and stage names. Never stores keys, SQL parameters or connection strings.
public sealed class RegistroTiempos
{
    private readonly Dictionary<string, double> durations = new();
    public string EtapaActual { get; private set; } = "inicio";
    public double TransactionMs { get; set; }
    public IReadOnlyDictionary<string, double> Duraciones => durations;

    public async Task<T> MeasureAsync<T>(string stage, Func<Task<T>> action)
    {
        EtapaActual = stage;
        var start = Stopwatch.GetTimestamp();
        try { return await action(); }
        finally { durations[stage] = Stopwatch.GetElapsedTime(start).TotalMilliseconds; }
    }

    public async Task MeasureAsync(string stage, Func<Task> action) =>
        await MeasureAsync(stage, async () => { await action(); return true; });
}
