using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NGB.BackgroundJobs.Contracts;
using NGB.BackgroundJobs.IntegrationTests.Infrastructure;
using NGB.BackgroundJobs.Jobs;
using NGB.Persistence.Readers.PostingState;
using NGB.PostgreSql.DependencyInjection;
using Npgsql;
using Xunit;

namespace NGB.BackgroundJobs.IntegrationTests.Jobs;

[Collection(HangfirePostgresCollection.Name)]
public sealed class AccountingOperationsStuckMonitorJob_PostgresEndToEnd_P0Tests(HangfirePostgresFixture fixture)
{
    [Fact]
    public async Task RunAsync_WhenNoStaleRows_ReportsProblem0AndZeroWarnings()
    {
        await ClearPostingLogAsync();

        // Insert a fresh in-progress row that must NOT be classified as stale.
        await InsertPostingLogRowAsync(
            documentId: Guid.CreateVersion7(),
            operation: 1,
            startedAtUtc: DateTime.UtcNow.AddMinutes(-5),
            completedAtUtc: null);

        using var sp = BuildServiceProvider(fixture.ConnectionString);
        await using var scope = sp.CreateAsyncScope();

        var reader = scope.ServiceProvider.GetRequiredService<IPostingStateReader>();
        var metrics = new TestJobRunMetrics();

        var job = new AccountingOperationsStuckMonitorJob(
            reader,
            NullLogger<AccountingOperationsStuckMonitorJob>.Instance,
            metrics);

        await job.RunAsync(CancellationToken.None);

        var snapshot = metrics.Snapshot();
        snapshot["stale_count"].Should().Be(0);
        snapshot["problem"].Should().Be(0);
        snapshot["warnings_logged"].Should().Be(0);
        snapshot["stale_rows_logged"].Should().Be(0);

        snapshot["page_size"].Should().Be(25);
        snapshot["stale_after_minutes"].Should().Be(10);
        snapshot["lookback_days"].Should().Be(30);
    }

    [Fact]
    public async Task RunAsync_WhenManyStaleRows_ReportsProblem1AndIsBoundedToFirstPage()
    {
        await ClearPostingLogAsync();

        // Insert > PageSize stale in-progress rows.
        var staleStartedAt = DateTime.UtcNow.AddMinutes(-20);
        for (var i = 0; i < 30; i++)
        {
            await InsertPostingLogRowAsync(
                documentId: Guid.CreateVersion7(),
                operation: 1,
                startedAtUtc: staleStartedAt.AddSeconds(-i),
                completedAtUtc: null);
        }

        using var sp = BuildServiceProvider(fixture.ConnectionString);
        await using var scope = sp.CreateAsyncScope();

        var reader = scope.ServiceProvider.GetRequiredService<IPostingStateReader>();
        var metrics = new TestJobRunMetrics();

        var job = new AccountingOperationsStuckMonitorJob(
            reader,
            NullLogger<AccountingOperationsStuckMonitorJob>.Instance,
            metrics);

        await job.RunAsync(CancellationToken.None);

        var snapshot = metrics.Snapshot();

        // The job reads only the first page (PageSize=25).
        snapshot["stale_count"].Should().Be(25);
        snapshot["stale_rows_logged"].Should().Be(25);
        snapshot["warnings_logged"].Should().Be(25);
        snapshot["problem"].Should().Be(1);

        snapshot["page_size"].Should().Be(25);
        snapshot["stale_after_minutes"].Should().Be(10);
    }

    private async Task ClearPostingLogAsync()
    {
        await using var conn = new NpgsqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();

        await conn.ExecuteAsync("TRUNCATE TABLE accounting_posting_state;");
    }

    private async Task InsertPostingLogRowAsync(Guid documentId, short operation, DateTime startedAtUtc, DateTime? completedAtUtc)
    {
        startedAtUtc = startedAtUtc.Kind == DateTimeKind.Utc
            ? startedAtUtc
            : DateTime.SpecifyKind(startedAtUtc, DateTimeKind.Utc);

        if (completedAtUtc is not null && completedAtUtc.Value.Kind != DateTimeKind.Utc)
            completedAtUtc = DateTime.SpecifyKind(completedAtUtc.Value, DateTimeKind.Utc);

        await using var conn = new NpgsqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();

        await conn.ExecuteAsync(
            """
            INSERT INTO accounting_posting_state (
                document_id,
                operation,
                started_at_utc,
                completed_at_utc
            ) VALUES (
                @DocumentId,
                @Operation,
                @StartedAtUtc,
                @CompletedAtUtc
            );
            """,
            new
            {
                DocumentId = documentId,
                Operation = operation,
                StartedAtUtc = startedAtUtc,
                CompletedAtUtc = completedAtUtc
            });
    }

    private static ServiceProvider BuildServiceProvider(string connectionString)
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddNgbPostgres(connectionString);

        return services.BuildServiceProvider();
    }

    private sealed class TestJobRunMetrics : IJobRunMetrics
    {
        private readonly Dictionary<string, long> _counters = new(StringComparer.Ordinal);

        public void Increment(string name, long delta = 1)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;
            if (delta == 0)
                return;

            name = name.Trim();

            _counters.TryGetValue(name, out var current);
            _counters[name] = current + delta;
        }

        public void Set(string name, long value)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;

            _counters[name.Trim()] = value;
        }

        public IReadOnlyDictionary<string, long> Snapshot() => new Dictionary<string, long>(_counters);
    }
}
