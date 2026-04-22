using System.Security.Cryptography;
using System.Text;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.OperationalRegisters;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

/// <summary>
/// P0: Admin endpoint can "ensure" (create/repair) per-register physical tables and returns health after remediation.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisterAdminEndpoint_EnsurePhysicalSchema_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task EnsurePhysicalSchemaById_WhenNoTablesExist_CreatesAndReturnsOk()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var registerId = await CreateRegisterWithSingleResourceAsync(host, code: UniqueCode("RR"), name: "Rent Roll", CancellationToken.None);

        await using var scope = host.Services.CreateAsyncScope();
        var endpoint = scope.ServiceProvider.GetRequiredService<IOperationalRegisterAdminEndpoint>();

        // Pre-check: health should indicate missing physical tables.
        var before = await endpoint.GetPhysicalSchemaHealthByIdAsync(registerId, CancellationToken.None);
        before.Should().NotBeNull();
        before!.IsOk.Should().BeFalse();
        before.Movements.Exists.Should().BeFalse();

        // Act: ensure (admin remediation).
        var after = await endpoint.EnsurePhysicalSchemaByIdAsync(registerId, CancellationToken.None);
        after.Should().NotBeNull();
        after!.IsOk.Should().BeTrue();

        after.Movements.Exists.Should().BeTrue();
        after.Movements.HasAppendOnlyGuard.Should().BeTrue();
        after.Movements.MissingColumns.Should().BeEmpty();
        after.Movements.MissingIndexes.Should().BeEmpty();

        after.Turnovers.Exists.Should().BeTrue();
        after.Turnovers.MissingColumns.Should().BeEmpty();
        after.Turnovers.MissingIndexes.Should().BeEmpty();

        after.Balances.Exists.Should().BeTrue();
        after.Balances.MissingColumns.Should().BeEmpty();
        after.Balances.MissingIndexes.Should().BeEmpty();
    }


    [Fact]
    public async Task EnsurePhysicalSchemaById_WhenMovementsResourceColumnDropped_RestoresColumn()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (registerId, tableCode) = await CreateRegisterAndEnsureSchemaAsync(host, UniqueCode("RR"), "Rent Roll", resources: 1, CancellationToken.None);
        var movementsTable = OperationalRegisterNaming.MovementsTable(tableCode);

        // Drop a resource column from movements.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            await uow.Connection.ExecuteAsync(
                $"ALTER TABLE {movementsTable} DROP COLUMN amount;",
                transaction: uow.Transaction);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var endpoint = scope.ServiceProvider.GetRequiredService<IOperationalRegisterAdminEndpoint>();

            var before = await endpoint.GetPhysicalSchemaHealthByIdAsync(registerId, CancellationToken.None);
            before.Should().NotBeNull();
            before!.IsOk.Should().BeFalse();
            before.Movements.MissingColumns.Should().Contain("amount");

            var after = await endpoint.EnsurePhysicalSchemaByIdAsync(registerId, CancellationToken.None);
            after.Should().NotBeNull();
            after!.IsOk.Should().BeTrue();
            after.Movements.MissingColumns.Should().NotContain("amount");
        }
    }

    [Fact]
    public async Task EnsurePhysicalSchemaById_WhenAppendOnlyGuardDropped_RestoresGuard()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (registerId, tableCode) = await CreateRegisterAndEnsureSchemaAsync(host, UniqueCode("RR"), "Rent Roll", resources: 1, CancellationToken.None);
        var movementsTable = OperationalRegisterNaming.MovementsTable(tableCode);

        // Drop append-only guard trigger(s) from the movements table.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            await uow.Connection.ExecuteAsync(
                $"""
                DO $$
                DECLARE r record;
                BEGIN
                    FOR r IN
                        SELECT t.tgname
                        FROM pg_trigger t
                        JOIN pg_proc p ON p.oid = t.tgfoid
                        WHERE t.tgrelid = '{movementsTable}'::regclass
                          AND NOT t.tgisinternal
                          AND p.proname = 'ngb_forbid_mutation_of_append_only_table'
                    LOOP
                        EXECUTE format('DROP TRIGGER %I ON %I', r.tgname, '{movementsTable}');
                    END LOOP;
                END $$;
                """,
                transaction: uow.Transaction);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var endpoint = scope.ServiceProvider.GetRequiredService<IOperationalRegisterAdminEndpoint>();

            var before = await endpoint.GetPhysicalSchemaHealthByIdAsync(registerId, CancellationToken.None);
            before.Should().NotBeNull();
            before!.IsOk.Should().BeFalse();
            before.Movements.HasAppendOnlyGuard.Should().BeFalse();

            var after = await endpoint.EnsurePhysicalSchemaByIdAsync(registerId, CancellationToken.None);
            after.Should().NotBeNull();
            after!.IsOk.Should().BeTrue();
            after.Movements.HasAppendOnlyGuard.Should().BeTrue();
        }
    }

    [Fact]
    public async Task EnsurePhysicalSchemaById_WhenTurnoversMonthIndexDropped_RestoresIndex()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (registerId, tableCode) = await CreateRegisterAndEnsureSchemaAsync(host, UniqueCode("RR"), "Rent Roll", resources: 1, CancellationToken.None);
        var turnoversTable = OperationalRegisterNaming.TurnoversTable(tableCode);

        // Drop the derived table index(period_month).
        var ixMonth = IxOpreg(turnoversTable, "month");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            await uow.Connection.ExecuteAsync(
                $"DROP INDEX IF EXISTS {ixMonth};",
                transaction: uow.Transaction);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var endpoint = scope.ServiceProvider.GetRequiredService<IOperationalRegisterAdminEndpoint>();

            var before = await endpoint.GetPhysicalSchemaHealthByIdAsync(registerId, CancellationToken.None);
            before.Should().NotBeNull();
            before!.IsOk.Should().BeFalse();
            before.Turnovers.MissingIndexes.Should().Contain(x => x.Contains("index(period_month)", StringComparison.OrdinalIgnoreCase));

            var after = await endpoint.EnsurePhysicalSchemaByIdAsync(registerId, CancellationToken.None);
            after.Should().NotBeNull();
            after!.IsOk.Should().BeTrue();
            after.Turnovers.MissingIndexes.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task EnsurePhysicalSchemaForAll_WhenMultipleRegisters_CreatesAllAndReturnsOk()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await CreateRegisterWithSingleResourceAsync(host, UniqueCode("RR"), "Rent Roll", CancellationToken.None);
        await CreateRegisterWithSingleResourceAsync(host, UniqueCode("AR"), "Accounts Receivable", CancellationToken.None);

        await using var scope = host.Services.CreateAsyncScope();
        var endpoint = scope.ServiceProvider.GetRequiredService<IOperationalRegisterAdminEndpoint>();

        var report = await endpoint.EnsurePhysicalSchemaForAllAsync(CancellationToken.None);

        report.TotalCount.Should().Be(2);
        report.OkCount.Should().Be(2);
        report.Items.Should().HaveCount(2);
        report.Items.Should().OnlyContain(x => x.IsOk);
    }

    [Fact]
    public async Task EnsurePhysicalSchemaById_ConcurrentCalls_DoNotThrow_AndReturnOk()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        // Arrange: register + many resources to slow down EnsureSchema and exercise the per-register schema lock.
        var registerId = await CreateRegisterWithManyResourcesAsync(host, UniqueCode("RR"), "Rent Roll", resources: 12, CancellationToken.None);

        const int workers = 6;

        var allReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = 0;

        async Task Worker()
        {
            await using var scope = host.Services.CreateAsyncScope();
            var endpoint = scope.ServiceProvider.GetRequiredService<IOperationalRegisterAdminEndpoint>();

            if (Interlocked.Increment(ref ready) == workers)
                allReady.TrySetResult();

            await allReady.Task;
            await go.Task;

            var health = await endpoint.EnsurePhysicalSchemaByIdAsync(registerId, CancellationToken.None);
            health.Should().NotBeNull();
            health!.IsOk.Should().BeTrue();
        }

        var tasks = Enumerable.Range(0, workers).Select(_ => Worker()).ToArray();

        await allReady.Task;
        go.TrySetResult();

        Func<Task> act = () => Task.WhenAll(tasks);
        await act.Should().NotThrowAsync();
    }


    private static async Task<Guid> CreateRegisterWithSingleResourceAsync(
        IHost host,
        string code,
        string name,
        CancellationToken ct)
        => await CreateRegisterWithManyResourcesAsync(host, code, name, resources: 1, ct);

    private static async Task<Guid> CreateRegisterWithManyResourcesAsync(
        IHost host,
        string code,
        string name,
        int resources,
        CancellationToken ct)
    {
        if (resources <= 0)
            throw new NgbArgumentOutOfRangeException(nameof(resources), resources, "Resources count must be positive.");

        await using var scope = host.Services.CreateAsyncScope();
        var mgmt = scope.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();

        var registerId = await mgmt.UpsertAsync(code, name, ct);

        var defs = new List<OperationalRegisterResourceDefinition>(resources);

        // First resource uses the canonical "amount" column to simplify DDL expectations.
        defs.Add(new OperationalRegisterResourceDefinition("Amount", "Amount", Ordinal: 10));

        for (var i = 2; i <= resources; i++)
            defs.Add(new OperationalRegisterResourceDefinition($"AMOUNT_{i:00}", $"Amount {i:00}", Ordinal: i * 10));

        await mgmt.ReplaceResourcesAsync(registerId, defs, ct);
        return registerId;
    }

    private static async Task<(Guid RegisterId, string TableCode)> CreateRegisterAndEnsureSchemaAsync(
        IHost host,
        string code,
        string name,
        int resources,
        CancellationToken ct)
    {
        var registerId = await CreateRegisterWithManyResourcesAsync(host, code, name, resources, ct);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var movements = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsStore>();
            var turnovers = scope.ServiceProvider.GetRequiredService<IOperationalRegisterTurnoversStore>();
            var balances = scope.ServiceProvider.GetRequiredService<IOperationalRegisterBalancesStore>();

            await movements.EnsureSchemaAsync(registerId, ct);
            await turnovers.EnsureSchemaAsync(registerId, ct);
            await balances.EnsureSchemaAsync(registerId, ct);

            var repo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>();
            var reg = await repo.GetByIdAsync(registerId, ct);
            reg.Should().NotBeNull();

            return (registerId, reg!.TableCode);
        }
    }

    private static string IxOpreg(string table, string purpose)
        => "ix_opreg_t_" + Hash8(table + "|" + purpose);

    private static string Hash8(string s)
        => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant()[..8];

    private static string UniqueCode(string prefix)
        => $"{prefix}_{Guid.NewGuid():N}"[..Math.Min(30, prefix.Length + 1 + 32)];
}
