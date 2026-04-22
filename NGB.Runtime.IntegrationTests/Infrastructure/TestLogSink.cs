using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

internal sealed record TestLogRecord(
    DateTime TimestampUtc,
    LogLevel Level,
    string Category,
    EventId EventId,
    string Message,
    Exception? Exception);

internal sealed class TestLogSink : ILoggerProvider
{
    private readonly ConcurrentQueue<TestLogRecord> _records = new();

    public IReadOnlyCollection<TestLogRecord> Records => _records.ToArray();

    public ILogger CreateLogger(string categoryName) => new SinkLogger(categoryName, _records);

    public void Dispose()
    {
        // No unmanaged resources.
    }

    private sealed class SinkLogger : ILogger
    {
        private readonly string _category;
        private readonly ConcurrentQueue<TestLogRecord> _records;

        public SinkLogger(string category, ConcurrentQueue<TestLogRecord> records)
        {
            _category = category;
            _records = records;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var message = formatter(state, exception);

            _records.Enqueue(new TestLogRecord(
                TimestampUtc: DateTime.UtcNow,
                Level: logLevel,
                Category: _category,
                EventId: eventId,
                Message: message,
                Exception: exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
