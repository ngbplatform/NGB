using Microsoft.Extensions.Logging;

namespace NGB.BackgroundJobs.Tests.TestDoubles;

public sealed record LogRecord(
    LogLevel Level,
    string Message,
    string? Template,
    IReadOnlyDictionary<string, object?> State,
    Exception? Exception);

/// <summary>
/// Minimal ILogger that records structured state (including {OriginalFormat})
/// so unit tests can assert strongly on log templates and parameters.
/// </summary>
public sealed class RecordingLogger<T> : ILogger<T>
{
    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }

    private readonly List<LogRecord> _records = new();

    public IReadOnlyList<LogRecord> Records => _records;

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);

        IReadOnlyDictionary<string, object?> dict;
        string? template = null;

        if (state is IEnumerable<KeyValuePair<string, object?>> kvs)
        {
            var d = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var kv in kvs)
                d[kv.Key] = kv.Value;

            dict = d;
            if (d.TryGetValue("{OriginalFormat}", out var fmt) && fmt is string s)
                template = s;
        }
        else
        {
            dict = new Dictionary<string, object?>(0);
        }

        _records.Add(new LogRecord(logLevel, message, template, dict, exception));
    }
}
