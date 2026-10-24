using System.Text.Json;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using NGB.BackgroundJobs.Infrastructure;
using NGB.BackgroundJobs.PostgreSql;
using NGB.PostgreSql.DependencyInjection;
using NGB.Runtime.DependencyInjection;
using CertificationApp.PostgreSql;
using CertificationApp.Runtime;

var connectionString = Environment.GetEnvironmentVariable("NGB_CONNECTION_STRING")
    ?? throw new InvalidOperationException("NGB_CONNECTION_STRING is required.");
var storage = PostgresHangfireJobStorageFactory.Create(connectionString, "hangfire", true);
using var storageLifetime = storage as IDisposable;

if (args is ["enqueue", var statePath])
{
    var operationId = Guid.NewGuid();
    var client = new BackgroundJobClient(storage);
    var schemaJobId = client.Enqueue<PlatformHangfireJobRunner>(runner =>
        runner.RunAsync("platform.schema.validate", JobCancellationToken.Null));
    var customJobId = client.Enqueue<CheckpointService>(service =>
        service.CaptureAsync(operationId, CancellationToken.None));
    File.WriteAllText(statePath, JsonSerializer.Serialize(new JobCheckpoint(operationId, schemaJobId, customJobId)));
    Console.WriteLine("Queued the platform schema job and custom orchestration checkpoint.");
    return;
}

if (args is ["verify", var checkpointPath])
{
    var checkpoint = JsonSerializer.Deserialize<JobCheckpoint>(File.ReadAllText(checkpointPath))
        ?? throw new InvalidOperationException("The job checkpoint is required.");
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddNgbRuntime().AddNgbPostgres(connectionString);
    services.AddCheckpointPostgres();
    services.AddScoped<CheckpointService>();
    await using var provider = services.BuildServiceProvider();
    await using var scope = provider.CreateAsyncScope();
    var service = scope.ServiceProvider.GetRequiredService<CheckpointService>();
    using var connection = storage.GetConnection();
    foreach (var jobId in new[] { checkpoint.SchemaJobId, checkpoint.CustomJobId })
    {
        var state = connection.GetStateData(jobId)?.Name;
        if (state != "Succeeded")
            throw new InvalidOperationException($"Job {jobId} did not succeed: {state}.");
    }

    if (await service.GetEffectCountAsync(checkpoint.OperationId, CancellationToken.None) != 1)
        throw new InvalidOperationException("The custom job effect must exist exactly once.");

    await service.CaptureAsync(checkpoint.OperationId, CancellationToken.None);
    if (await service.GetEffectCountAsync(checkpoint.OperationId, CancellationToken.None) != 1)
        throw new InvalidOperationException("Retry duplicated the custom job effect.");

    Console.WriteLine("Platform schema job, custom orchestration, provider persistence and idempotent retry passed.");
    return;
}

throw new ArgumentException("Usage: CertificationApp.Probe enqueue|verify <checkpoint.json>");

internal sealed record JobCheckpoint(Guid OperationId, string SchemaJobId, string CustomJobId);
