using System.Text.Json;
using Hangfire;
using NGB.BackgroundJobs.Infrastructure;
using NGB.BackgroundJobs.PostgreSql;
using Npgsql;
using Xunit;

namespace ExternalConsumer.Tests;

public sealed class RuntimeStateTests
{
    [Fact]
    public async Task SnapshotPersistentApplicationState()
    {
        var connectionString = Environment.GetEnvironmentVariable("NGB_CONNECTION_STRING");
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        await using var tablesCommand = new NpgsqlCommand(
            "SELECT tablename FROM pg_tables WHERE schemaname = 'public' ORDER BY tablename", connection, transaction);
        var tables = new List<string>();
        await using (var reader = await tablesCommand.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                tables.Add(reader.GetString(0));
        }

        Assert.Contains("migration_changelog__platform", tables);
        var state = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var identifiers = new NpgsqlCommandBuilder();
        foreach (var table in tables)
        {
            var quoted = identifiers.QuoteIdentifier(table);
            await using var command = new NpgsqlCommand(
                $"SELECT COALESCE(jsonb_agg(to_jsonb(r) ORDER BY to_jsonb(r)::text), '[]'::jsonb)::text FROM public.{quoted} r",
                connection, transaction);
            state.Add(table, (string)(await command.ExecuteScalarAsync())!);
        }

        await File.WriteAllTextAsync(
            Environment.GetEnvironmentVariable("NGB_PROBE_STATE")!, JsonSerializer.Serialize(state));
        await transaction.CommitAsync();
    }

    [Fact]
    public void EnqueueRegisteredPlatformJob()
    {
        var storage = PostgresHangfireJobStorageFactory.Create(
            Environment.GetEnvironmentVariable("NGB_CONNECTION_STRING")!, "hangfire", true);
        using var lifetime = storage as IDisposable;
        var client = new BackgroundJobClient(storage);
        var jobId = client.Enqueue<PlatformHangfireJobRunner>(runner =>
            runner.RunAsync("platform.schema.validate", JobCancellationToken.Null));
        Assert.False(string.IsNullOrWhiteSpace(jobId));
        File.WriteAllText(Environment.GetEnvironmentVariable("NGB_PROBE_STATE")!, jobId);
    }

    [Fact]
    public async Task RegisteredPlatformJobSucceedsThroughWorker()
    {
        var storage = PostgresHangfireJobStorageFactory.Create(
            Environment.GetEnvironmentVariable("NGB_CONNECTION_STRING")!, "hangfire", true);
        using var lifetime = storage as IDisposable;
        var jobId = await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("NGB_PROBE_STATE")!);
        using var connection = storage.GetConnection();
        for (var attempt = 0; attempt < 90; attempt++)
        {
            var state = connection.GetStateData(jobId)?.Name;
            Assert.NotEqual("Failed", state);
            if (state == "Succeeded")
                return;

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        Assert.Fail($"Platform job {jobId} did not succeed within its deadline.");
    }
}
