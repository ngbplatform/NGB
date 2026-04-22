using System.Security.Cryptography;
using System.Text;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NGB.BackgroundJobs.Contracts;
using NGB.BackgroundJobs.IntegrationTests.Infrastructure;
using NGB.BackgroundJobs.Jobs;
using NGB.OperationalRegisters;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.DependencyInjection;
using NGB.Runtime.DependencyInjection;
using NGB.Runtime.OperationalRegisters;
using Xunit;

namespace NGB.BackgroundJobs.IntegrationTests.Jobs;

[Collection(HangfirePostgresCollection.Name)]
public sealed class OperationalRegistersEnsureSchemaJob_PostgresEndToEnd_P0Tests(HangfirePostgresFixture fixture)
{
    [Fact]
    public async Task RunAsync_WhenAppendOnlyGuardOrIndexesMissing_RepairsAndReportsHealthy()
    {
        var suffix = Guid.CreateVersion7().ToString("N")[..12].ToUpperInvariant();
        var code = $"OR_BGJOB_ENSURE_{suffix}";

        // For Operational Registers, per-register physical tables are derived from table_code.
        var tableCode = OperationalRegisterNaming.NormalizeTableCode(code);
        var movementsTable = OperationalRegisterNaming.MovementsTable(tableCode);

        var trgAppendOnly = Trg(movementsTable, "append_only");
        var ixDoc = Ix(movementsTable, "doc");

        Guid registerId;

        using var sp = BuildServiceProvider(fixture.ConnectionString);

        // 1) Create register.
        await using (var scope = sp.CreateAsyncScope())
        {
            var mgmt = scope.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();
            registerId = await mgmt.UpsertAsync(code, name: "BGJ ensure schema test", ct: CancellationToken.None);
            registerId.Should().NotBe(Guid.Empty);
        }

        // 2) Ensure schema once (creates tables + indexes + append-only trigger).
        await using (var scope = sp.CreateAsyncScope())
        {
            var maintenance = scope.ServiceProvider.GetRequiredService<IOperationalRegisterAdminMaintenanceService>();
            var health = await maintenance.EnsurePhysicalSchemaByIdAsync(registerId, CancellationToken.None);
            health.Should().NotBeNull();
            health!.IsOk.Should().BeTrue();
        }

        // 3) Break schema: drop append-only trigger + the deterministic index(document_id).
        await using (var scope = sp.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            var trgExists = await uow.Connection.ExecuteScalarAsync<int>(
                new CommandDefinition(
                    """
                    SELECT COUNT(1)
                    FROM pg_trigger
                    WHERE tgname = @TriggerName
                      AND tgrelid = @Table::regclass
                      AND NOT tgisinternal;
                    """,
                    new { TriggerName = trgAppendOnly, Table = movementsTable },
                    transaction: uow.Transaction,
                    cancellationToken: CancellationToken.None));

            trgExists.Should().Be(1, "initial schema ensure must create append-only trigger {0} on {1}", trgAppendOnly, movementsTable);

            await uow.Connection.ExecuteAsync(
                new CommandDefinition(
                    $"DROP TRIGGER IF EXISTS {trgAppendOnly} ON {movementsTable};",
                    transaction: uow.Transaction,
                    cancellationToken: CancellationToken.None));

            var ixExists = await uow.Connection.ExecuteScalarAsync<int>(
                new CommandDefinition(
                    """
                    SELECT COUNT(1)
                    FROM pg_indexes
                    WHERE schemaname = 'public'
                      AND tablename = @TableName
                      AND indexname = @IndexName;
                    """,
                    new { TableName = movementsTable, IndexName = ixDoc },
                    transaction: uow.Transaction,
                    cancellationToken: CancellationToken.None));

            ixExists.Should().Be(1, "initial schema ensure must create index {0} on {1}", ixDoc, movementsTable);

            await uow.Connection.ExecuteAsync(
                new CommandDefinition(
                    $"DROP INDEX IF EXISTS {ixDoc};",
                    transaction: uow.Transaction,
                    cancellationToken: CancellationToken.None));
        }

        // 4) Sanity: schema is unhealthy before the job.
        await using (var scope = sp.CreateAsyncScope())
        {
            var healthReader = scope.ServiceProvider.GetRequiredService<IOperationalRegisterPhysicalSchemaHealthReader>();
            var before = await healthReader.GetByRegisterIdAsync(registerId, CancellationToken.None);

            before.Should().NotBeNull();
            before!.IsOk.Should().BeFalse();

            before.Movements.Exists.Should().BeTrue();
            before.Movements.HasAppendOnlyGuard.Should().BeFalse();
            before.Movements.MissingIndexes.Should().NotBeEmpty();
            before.Movements.MissingIndexes.Should().Contain("index(document_id)");
        }

        // 5) Run the job: it should repair schema and report OK via metrics.
        var metrics = new TestJobRunMetrics();

        await using (var scope = sp.CreateAsyncScope())
        {
            var maintenance = scope.ServiceProvider.GetRequiredService<IOperationalRegisterAdminMaintenanceService>();

            var job = new OperationalRegistersEnsureSchemaJob(
                maintenance,
                NullLogger<OperationalRegistersEnsureSchemaJob>.Instance,
                metrics);

            await job.RunAsync(CancellationToken.None);
        }

        var snapshot = metrics.Snapshot();
        snapshot.Should().ContainKey("registers_total");
        snapshot["registers_total"].Should().BeGreaterThan(0);
        snapshot["registers_ok"].Should().Be(snapshot["registers_total"]);
        snapshot["registers_failed"].Should().Be(0);
        snapshot["has_failures"].Should().Be(0);

        // 6) Verify schema is healthy again.
        await using (var scope = sp.CreateAsyncScope())
        {
            var healthReader = scope.ServiceProvider.GetRequiredService<IOperationalRegisterPhysicalSchemaHealthReader>();
            var after = await healthReader.GetByRegisterIdAsync(registerId, CancellationToken.None);

            after.Should().NotBeNull();
            after!.IsOk.Should().BeTrue();

            after.Movements.Exists.Should().BeTrue();
            after.Movements.MissingColumns.Should().BeEmpty();
            after.Movements.MissingIndexes.Should().BeEmpty();
            after.Movements.HasAppendOnlyGuard.Should().BeTrue();
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

    private static string Ix(string table, string purpose)
        => "ix_opreg_" + purpose + "_" + Hash8(table + "|" + purpose);

    private static string Trg(string table, string purpose)
        => "trg_opreg_" + purpose + "_" + Hash8(table + "|" + purpose);

    private static string Hash8(string s)
        => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant()[..8];
}
