using System.Diagnostics;

namespace NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;

internal sealed class NpgsqlCommandCounter : IDisposable
{
    private Measurement? _active;
    private readonly ActivityListener _listener;

    public NpgsqlCommandCounter()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = static source => source.Name.StartsWith("Npgsql", StringComparison.Ordinal),
            Sample = static (ref _) => ActivitySamplingResult.PropagationData,
            SampleUsingParentId = static (ref _) => ActivitySamplingResult.PropagationData,
            ActivityStarted = activity =>
            {
                var measurement = Volatile.Read(ref _active);
                if (measurement is null
                    || activity.OperationName.StartsWith("CONNECT ", StringComparison.Ordinal))
                    return;

                Interlocked.Increment(ref measurement.Count);
                lock (measurement.Names)
                    measurement.Names.Add(activity.OperationName);
            }
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public async Task<SqlCommandMeasurement> CountAsync(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var measurement = new Measurement();
        if (Interlocked.CompareExchange(ref _active, measurement, null) is not null)
            throw new InvalidOperationException("Nested SQL command measurements are not supported.");

        try
        {
            await action();
        }
        finally
        {
            Volatile.Write(ref _active, null);
        }

        lock (measurement.Names)
            return new SqlCommandMeasurement(measurement.Count, measurement.Names.ToArray());
    }

    public void Dispose() => _listener.Dispose();

    private sealed class Measurement
    {
        public int Count;
        public List<string> Names { get; } = [];
    }
}

internal sealed record SqlCommandMeasurement(int Count, IReadOnlyList<string> ActivityNames);
