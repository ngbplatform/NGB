using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace NGB.Testing.Reporting;

/// <summary>Opt-in driver tracing for audits. Never records bind values or connection strings.</summary>
internal sealed class ReportPerformanceProbe : IDisposable
{
    private static readonly ConcurrentDictionary<ActivityTraceId, ReportPerformanceProbe> Active = new();
    private static readonly object OutputLock = new();
    private static readonly ActivityListener Listener = StartListener();
    private readonly ConcurrentQueue<(string Sql, double Ms)> _commands = new();
    private readonly Activity _activity;
    private readonly Stopwatch _watch = Stopwatch.StartNew();
    private readonly long _allocated = GC.GetTotalAllocatedBytes(false);
    private readonly string _code;
    private readonly string _scenario;

    public static bool Enabled => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NGB_REPORT_AUDIT_DIR"));
    public int CommandCount => _commands.Count;

    public IReadOnlyList<string> SqlCommands => _commands.Select(x => x.Sql).ToArray();

    public long? Rows { get; set; }
    public long? Bytes { get; set; }
    public string? Error { get; set; }

    public ReportPerformanceProbe(string code, string scenario)
    {
        _code = code; _scenario = scenario;
        _activity = new Activity("report-performance-audit").SetIdFormat(ActivityIdFormat.W3C);
        // Separate roots prevent nested report calls from attributing commands to another sample.
        _activity.SetParentId(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom());
        _activity.Start();
        Active[_activity.TraceId] = this;
    }

    public static ReportPerformanceProbe? Begin(string code, string scenario)
        => Enabled ? new(code, scenario) : null;

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _watch.Stop();
        Active.TryRemove(_activity.TraceId, out _);
        _activity.Stop();
        var directory = Environment.GetEnvironmentVariable("NGB_REPORT_AUDIT_DIR");

        if (string.IsNullOrWhiteSpace(directory))
            return;

        Directory.CreateDirectory(directory);
        var commands = _commands.ToArray();
        var groups = commands
            .GroupBy(c => c.Sql)
            .Select(g => new
            {
                hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(g.Key)))[..16],
                count = g.Count(),
                ms = g.Sum(c => c.Ms),
                sql = g.Key
            })
            .ToArray();

        var record = new
        { 
            report = _code,
            scenario = _scenario,
            ms = _watch.Elapsed.TotalMilliseconds,
            sqlCommands = commands.Length,
            sqlMs = commands.Sum(c => c.Ms),
            rows = Rows,
            bytes = Bytes,
            allocatedBytes = GC.GetTotalAllocatedBytes(false) - _allocated,
            workingSetBytes = Environment.WorkingSet,
            error = Error,
            statements = groups
        };

        lock (OutputLock)
        {
            File.AppendAllText(
                Path.Combine(directory, typeof(ReportPerformanceProbe).Assembly.GetName().Name + ".jsonl"),
                JsonSerializer.Serialize(record) + "\n");
        }
    }

    private static ActivityListener StartListener()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Npgsql", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => Enabled || !Active.IsEmpty
                ? ActivitySamplingResult.AllData
                : ActivitySamplingResult.None,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => Enabled || !Active.IsEmpty
                ? ActivitySamplingResult.AllData
                : ActivitySamplingResult.None,
            ActivityStopped = activity =>
            {
                if (!Active.TryGetValue(activity.TraceId, out var probe))
                    return;

                var sql = activity.GetTagItem("db.query.text")?.ToString()
                    ?? activity.GetTagItem("db.statement")?.ToString();

                if (!string.IsNullOrWhiteSpace(sql))
                    probe._commands.Enqueue((sql, activity.Duration.TotalMilliseconds));
            }
        };

        ActivitySource.AddActivityListener(listener);

        return listener;
    }
}

internal sealed class ReportAuditStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, continuation) =>
        {
            var path = context.Request.Path.Value ?? "";
            if (!path.StartsWith("/api/reports/", StringComparison.Ordinal) || context.Request.Method != "POST")
            {
                await continuation(); return;
            }

            using var probe = ReportPerformanceProbe.Begin(path.Split('/')[3], path.EndsWith("/xlsx") ? "http-export" : "http-page");
            try
            {
                await continuation();

                if (probe is not null)
                {
                    probe.Bytes = context.Response.ContentLength;
                    if (context.Response.StatusCode >= 400)
                        probe.Error = context.Response.StatusCode.ToString();
                }
            }
            catch (Exception e)
            {
                probe?.Error = e.GetType().Name;
                throw;
            }
        });

        next(app);
    };
}
