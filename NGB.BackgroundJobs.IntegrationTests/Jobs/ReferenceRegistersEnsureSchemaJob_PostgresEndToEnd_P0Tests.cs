using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NGB.BackgroundJobs.Contracts;
using NGB.BackgroundJobs.IntegrationTests.Infrastructure;
using NGB.BackgroundJobs.Jobs;
using NGB.Persistence.ReferenceRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.DependencyInjection;
using NGB.ReferenceRegisters;
using NGB.Runtime.DependencyInjection;
using NGB.Runtime.ReferenceRegisters;
using Xunit;

namespace NGB.BackgroundJobs.IntegrationTests.Jobs;

[Collection(HangfirePostgresCollection.Name)]
public sealed class ReferenceRegistersEnsureSchemaJob_PostgresEndToEnd_P0Tests(HangfirePostgresFixture fixture)
{
    [Fact]
    public async Task RunAsync_WhenAppendOnlyTriggerOrIndexesMissing_RepairsAndReportsHealthy()
    {
        var suffix = Guid.CreateVersion7().ToString("N")[..12].ToUpperInvariant();
        var code = $"RR_BGJOB_ENSURE_{suffix}";

        var tableCode = ReferenceRegisterNaming.NormalizeTableCode(code);
        var table = ReferenceRegisterNaming.RecordsTable(tableCode);

        Guid registerId;

        using var sp = BuildServiceProvider(fixture.ConnectionString);

        // 1) Create a register (auto-ensures schema).
        await using (var scope = sp.CreateAsyncScope())
        {
            var mgmt = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();

            registerId = await mgmt.UpsertAsync(
                code,
                name: "BGJ ensure schema test",
                periodicity: ReferenceRegisterPeriodicity.NonPeriodic,
                recordMode: ReferenceRegisterRecordMode.Independent,
                ct: CancellationToken.None);

            registerId.Should().NotBe(Guid.Empty);
        }

        // 2) Break schema: drop append-only trigger and 2 deterministic indexes.
        await using (var scope = sp.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            var trg = await uow.Connection.ExecuteScalarAsync<string?>(
                new CommandDefinition(
                    """
                    SELECT tgname
                    FROM pg_trigger
                    WHERE tgrelid = @Table::regclass
                      AND NOT tgisinternal
                      AND tgname LIKE 'trg_refreg_append_only_%'
                    LIMIT 1;
                    """,
                    new { Table = table },
                    transaction: uow.Transaction,
                    cancellationToken: CancellationToken.None));

            trg.Should().NotBeNull("initial schema ensure must create an append-only trigger on {0}", table);

            await uow.Connection.ExecuteAsync(
                new CommandDefinition(
                    $"DROP TRIGGER IF EXISTS {trg} ON {table};",
                    transaction: uow.Transaction,
                    cancellationToken: CancellationToken.None));

            var ixKeyV2 = await uow.Connection.ExecuteScalarAsync<string?>(
                new CommandDefinition(
                    """
                    SELECT indexname
                    FROM pg_indexes
                    WHERE schemaname = 'public'
                      AND tablename = @TableName
                      AND indexname LIKE 'ix_refreg_key_v2_%'
                    LIMIT 1;
                    """,
                    new { TableName = table },
                    transaction: uow.Transaction,
                    cancellationToken: CancellationToken.None));

            ixKeyV2.Should().NotBeNull("initial schema ensure must create ix_refreg_key_v2_* on {0}", table);

            await uow.Connection.ExecuteAsync(
                new CommandDefinition(
                    $"DROP INDEX IF EXISTS {ixKeyV2};",
                    transaction: uow.Transaction,
                    cancellationToken: CancellationToken.None));

        }

        // 3) Sanity: schema is unhealthy before the job.
        await using (var scope = sp.CreateAsyncScope())
        {
            var healthReader = scope.ServiceProvider.GetRequiredService<IReferenceRegisterPhysicalSchemaHealthReader>();
            var before = await healthReader.GetByRegisterIdAsync(registerId, CancellationToken.None);

            before.Should().NotBeNull();
            before!.IsOk.Should().BeFalse();
            before.Records.Exists.Should().BeTrue();
            before.Records.HasAppendOnlyGuard.Should().BeFalse();
            before.Records.MissingIndexes.Should().NotBeEmpty();
        }

        // 4) Run the job: it should repair schema and report OK via metrics.
        var metrics = new TestJobRunMetrics();

        await using (var scope = sp.CreateAsyncScope())
        {
            var maintenance = scope.ServiceProvider.GetRequiredService<IReferenceRegisterAdminMaintenanceService>();

            var job = new ReferenceRegistersEnsureSchemaJob(
                maintenance,
                NullLogger<ReferenceRegistersEnsureSchemaJob>.Instance,
                metrics);

            await job.RunAsync(CancellationToken.None);
        }

        var snapshot = metrics.Snapshot();
        snapshot.Should().ContainKey("registers_total");
        snapshot["registers_total"].Should().BeGreaterThan(0);
        snapshot["registers_ok"].Should().Be(snapshot["registers_total"]);
        snapshot["registers_failed"].Should().Be(0);
        snapshot["has_failures"].Should().Be(0);

        // 5) Verify schema is healthy again.
        await using (var scope = sp.CreateAsyncScope())
        {
            var healthReader = scope.ServiceProvider.GetRequiredService<IReferenceRegisterPhysicalSchemaHealthReader>();
            var after = await healthReader.GetByRegisterIdAsync(registerId, CancellationToken.None);

            after.Should().NotBeNull();
            after!.IsOk.Should().BeTrue();
            after.Records.Exists.Should().BeTrue();
            after.Records.MissingColumns.Should().BeEmpty();
            after.Records.MissingIndexes.Should().BeEmpty();
            after.Records.HasAppendOnlyGuard.Should().BeTrue();
        }
    }

    private static ServiceProvider BuildServiceProvider(string connectionString)
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddNgbPostgres(connectionString);
        services.AddNgbRuntime();

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
