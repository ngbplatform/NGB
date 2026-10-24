using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace NGB.Testing.Mcp;

internal sealed class McpQueryProbe : IDisposable
{
    private static readonly ConcurrentDictionary<ActivityTraceId, McpQueryProbe> Active = new();
    private static readonly object OutputLock = new();
    private static readonly ActivityListener Listener = CreateListener();
    private readonly Activity _activity;
    private readonly Stopwatch _watch = Stopwatch.StartNew();
    private readonly string _scenario;
    private int _commands;

    public McpQueryProbe(string scenario)
    {
        _scenario = scenario;
        _activity = new Activity("mcp-query-probe").SetIdFormat(ActivityIdFormat.W3C);
        _activity.SetParentId(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom());
        _activity.Start();
        Active[_activity.TraceId] = this;
    }

    public int CommandCount => Volatile.Read(ref _commands);

    public void Dispose()
    {
        _watch.Stop();
        Active.TryRemove(_activity.TraceId, out _);
        _activity.Dispose();
        var directory = Environment.GetEnvironmentVariable("NGB_MCP_PERFORMANCE_DIR");
        if (string.IsNullOrWhiteSpace(directory))
            return;

        Directory.CreateDirectory(directory);
        var sample = JsonSerializer.Serialize(new
        {
            scenario = _scenario,
            ms = _watch.Elapsed.TotalMilliseconds,
            sqlCommands = CommandCount
        });
        lock (OutputLock)
        {
            File.AppendAllText(Path.Combine(directory, typeof(McpQueryProbe).Assembly.GetName().Name + ".jsonl"), sample + "\n");
        }
    }

    private static ActivityListener CreateListener()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Npgsql", StringComparison.Ordinal)
                || source.Name is "System.Net.Http" or "Microsoft.AspNetCore"
                || source.Name.StartsWith("ModelContextProtocol", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                if (Active.TryGetValue(activity.TraceId, out var probe)
                    && (activity.GetTagItem("db.query.text") is not null || activity.GetTagItem("db.statement") is not null))
                {
                    Interlocked.Increment(ref probe._commands);
                }
            }
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }
}
