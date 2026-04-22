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
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

/// <summary>
/// P0: Admin endpoint returns physical schema health for per-register tables (opreg_&lt;table_code&gt;__*).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisterAdminEndpoint_PhysicalSchemaHealth_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task GetPhysicalSchemaHealthById_WhenSchemaEnsured_IsOk()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (registerId, tableCode) = await CreateRegisterAndEnsureSchemaAsync(host, CancellationToken.None);
        var movementsTable = OperationalRegisterNaming.MovementsTable(tableCode);

        await using var scope = host.Services.CreateAsyncScope();
        var endpoint = scope.ServiceProvider.GetRequiredService<IOperationalRegisterAdminEndpoint>();

        var health = await endpoint.GetPhysicalSchemaHealthByIdAsync(registerId, CancellationToken.None);
        health.Should().NotBeNull();
        health!.IsOk.Should().BeTrue();

        health.Movements.TableName.Should().Be(movementsTable);
        health.Movements.Exists.Should().BeTrue();
        health.Movements.HasAppendOnlyGuard.Should().BeTrue();
        health.Movements.MissingColumns.Should().BeEmpty();
        health.Movements.MissingIndexes.Should().BeEmpty();

        health.Turnovers.IsOk.Should().BeTrue();
        health.Turnovers.MissingColumns.Should().BeEmpty();
        health.Turnovers.MissingIndexes.Should().BeEmpty();

        health.Balances.IsOk.Should().BeTrue();
        health.Balances.MissingColumns.Should().BeEmpty();
        health.Balances.MissingIndexes.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPhysicalSchemaHealthById_WhenResourceColumnMissing_ReportsMissingColumns()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (registerId, tableCode) = await CreateRegisterAndEnsureSchemaAsync(host, CancellationToken.None);
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

            var health = await endpoint.GetPhysicalSchemaHealthByIdAsync(registerId, CancellationToken.None);
            health.Should().NotBeNull();
            health!.IsOk.Should().BeFalse();

            health.Movements.Exists.Should().BeTrue();
            health.Movements.MissingColumns.Should().Contain("amount");
        }
    }

    [Fact]
    public async Task GetPhysicalSchemaHealthById_WhenAppendOnlyGuardMissing_ReportsHasAppendOnlyGuardFalse()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (registerId, tableCode) = await CreateRegisterAndEnsureSchemaAsync(host, CancellationToken.None);
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

            var health = await endpoint.GetPhysicalSchemaHealthByIdAsync(registerId, CancellationToken.None);
            health.Should().NotBeNull();
            health!.IsOk.Should().BeFalse();

            health.Movements.Exists.Should().BeTrue();
            health.Movements.HasAppendOnlyGuard.Should().BeFalse();
            health.Movements.IsOk.Should().BeFalse();
        }
    }

    [Fact]
    public async Task GetPhysicalSchemaHealthById_WhenDerivedUniqueMissing_ReportsMissingUniqueIndex()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (registerId, tableCode) = await CreateRegisterAndEnsureSchemaAsync(host, CancellationToken.None);
        var turnoversTable = OperationalRegisterNaming.TurnoversTable(tableCode);

        // Drop UNIQUE(period_month, dimension_set_id) from turnovers.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            await uow.Connection.ExecuteAsync(
                $"""
                DO $$
                DECLARE cname text;
                BEGIN
                    SELECT conname INTO cname
                    FROM pg_constraint
                    WHERE conrelid = '{turnoversTable}'::regclass
                      AND contype = 'u'
                    LIMIT 1;

                    IF cname IS NOT NULL THEN
                        EXECUTE format('ALTER TABLE %I DROP CONSTRAINT %I', '{turnoversTable}', cname);
                    END IF;
                END $$;
                """,
                transaction: uow.Transaction);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var endpoint = scope.ServiceProvider.GetRequiredService<IOperationalRegisterAdminEndpoint>();

            var health = await endpoint.GetPhysicalSchemaHealthByIdAsync(registerId, CancellationToken.None);
            health.Should().NotBeNull();
            health!.IsOk.Should().BeFalse();

            health.Turnovers.Exists.Should().BeTrue();
            health.Turnovers.MissingIndexes.Should().Contain(x => x.Contains("unique(period_month, dimension_set_id)", StringComparison.OrdinalIgnoreCase));
        }
    }

    private static async Task<(Guid RegisterId, string TableCode)> CreateRegisterAndEnsureSchemaAsync(
        IHost host,
        CancellationToken ct)
    {
        Guid registerId;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var mgmt = scope.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();
            registerId = await mgmt.UpsertAsync("RR", "Rent Roll", ct);

            await mgmt.ReplaceResourcesAsync(
                registerId,
                new[]
                {
                    new OperationalRegisterResourceDefinition("Amount", "Amount", 10)
                },
                ct);

            // Ensure per-register physical tables.
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
}
