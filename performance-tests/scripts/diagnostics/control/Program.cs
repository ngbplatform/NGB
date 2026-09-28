using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Diagnostics.Tracing;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Hangfire;
using Hangfire.PostgreSql;
using Hangfire.PostgreSql.Factories;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;

// Test tooling only: uses the deployed worker's configuration and assemblies.
// No production endpoint, database schema change or alternate finalization implementation.
const string jobId = "opreg.finalization.run_dirty_months";
try
{
    if (args.Length == 1 && args[0] is "validate-worker" or "trigger")
    {
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            var path = Path.Combine("/app", name.Name + ".dll");
            return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
        };
        var type = Assembly.Load("NGB.BackgroundJobs").GetType("NGB.BackgroundJobs.Infrastructure.PlatformHangfireJobRunner", true)!;
        if (type.GetMethod("RunAsync") is null) throw new InvalidOperationException();
        if (args[0] == "validate-worker")
        {
            Emit(new { kind = "worker-ready", jobId });
            return 0;
        }
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Hangfire")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? throw new InvalidOperationException();
        var options = new PostgreSqlStorageOptions { PrepareSchemaIfNecessary = false, SchemaName = "hangfire" };
        var storage = new PostgreSqlStorage(new NpgsqlConnectionFactory(connectionString, options, null!), options);
        GlobalConfiguration.Configuration.UseSimpleAssemblyNameTypeSerializer();
        var id = new RecurringJobManager(storage).TriggerJob(jobId);
        if (string.IsNullOrEmpty(id)) throw new InvalidOperationException();
        Emit(new { kind = "triggered", jobId, id, at = DateTimeOffset.UtcNow });
        return 0;
    }
    if (args.Length == 1 && args[0] == "self-test")
    {
        using var meter = new Meter("NGB.Reporting");
        var requests = meter.CreateCounter<long>("ngb.report.admission.requests");
        var duration = meter.CreateHistogram<double>("ngb.report.admission.duration");
        meter.CreateObservableGauge("ngb.report.admission.active", () => 2);
        meter.CreateObservableGauge("ngb.report.admission.queued", () => 3);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var producer = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                requests.Add(1, new KeyValuePair<string, object?>("outcome", "acquired"));
                duration.Record(.01);
                await Task.Delay(100);
            }
        });
        Collect(Environment.ProcessId, stop.Token);
        await producer;
        return 0;
    }
    if (args.Length == 1 && args[0] == "meters")
    {
        // Resolve the API process, never assume PID 1 (entrypoint wrappers are allowed).
        var processes = Directory.GetDirectories("/proc")
            .Where(path => int.TryParse(Path.GetFileName(path), out _))
            .Where(path =>
            {
                try { return File.ReadAllText(Path.Combine(path, "cmdline")).Split('\0')
                    .Any(arg => Path.GetFileName(arg) == "NGB.PropertyManagement.Api.dll"); }
                catch (IOException) { return false; }
            }).Select(path => int.Parse(Path.GetFileName(path))).ToArray();
        if (processes.Length != 1) throw new InvalidOperationException();
        using var stop = new CancellationTokenSource();
        // docker exec -i: closing stdin ends the collector without signaling the API.
        _ = Task.Run(async () => { await Console.In.ReadLineAsync(); stop.Cancel(); });
        Collect(processes[0], stop.Token);
        return 0;
    }
    throw new ArgumentException();
}
catch (Exception error)
{
    // Exception messages/stack traces can include credentials or application data.
    Emit(new { kind = "error", errorType = error.GetType().Name });
    return 2;
}

static void Emit(object value) => Console.WriteLine(JsonSerializer.Serialize(value));

static void Collect(int pid, CancellationToken stop)
{
    var sessionId = Guid.NewGuid().ToString("N");
    var provider = new EventPipeProvider("System.Diagnostics.Metrics", EventLevel.Informational, 2,
        new Dictionary<string, string>
        {
            ["SessionId"] = sessionId, ["Metrics"] = "NGB.Reporting", ["RefreshInterval"] = "1",
            ["MaxTimeSeries"] = "100", ["MaxHistograms"] = "20"
        });
    using var session = new DiagnosticsClient(pid).StartEventPipeSession([provider], false);
    using var source = new EventPipeEventSource(session.EventStream);
    using var registration = stop.Register(() => { try { session.Stop(); } catch (ServerNotAvailableException) { } });
    source.Dynamic.All += data =>
    {
        if (data.ProviderName != "System.Diagnostics.Metrics") return;
        var payload = data.PayloadNames.ToDictionary(name => name, data.PayloadByName);
        if (payload.TryGetValue("sessionId", out var value) && !Equals(value, sessionId)) return;
        if (data.EventName.Contains("Error", StringComparison.Ordinal) || data.EventName.EndsWith("LimitReached", StringComparison.Ordinal))
            Emit(new { kind = "error", eventName = data.EventName, at = data.TimeStamp.ToUniversalTime() });
        else if (data.EventName is "CollectionStart" or "CollectionStop" ||
                 payload.TryGetValue("meterName", out var meter) && Equals(meter, "NGB.Reporting"))
        {
            payload.Remove("sessionId");
            Emit(new { kind = "meter", eventName = data.EventName, at = data.TimeStamp.ToUniversalTime(), payload });
        }
    };
    Emit(new { kind = "collector-ready", pid, at = DateTimeOffset.UtcNow });
    source.Process();
    if (source.EventsLost != 0) throw new InvalidOperationException("Metric events lost.");
    Emit(new { kind = "collector-stopped", at = DateTimeOffset.UtcNow });
}
