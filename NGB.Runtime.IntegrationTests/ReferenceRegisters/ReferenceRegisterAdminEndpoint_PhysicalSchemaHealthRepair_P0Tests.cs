using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Metadata.Base;
using NGB.Persistence.ReferenceRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.ReferenceRegisters;
using Xunit;

namespace NGB.Runtime.IntegrationTests.ReferenceRegisters;

/// <summary>
/// P0: Admin endpoint must accurately report RR physical schema drift (missing table/guard/indexes)
/// and repair it via EnsurePhysicalSchemaByIdAsync.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ReferenceRegisterAdminEndpoint_PhysicalSchemaHealthRepair_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task GetPhysicalSchemaHealthById_WhenRecordsTableMissing_ReportsNotOk_AndEnsureRepairs()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (registerId, recordsTable) = await CreateRegisterAndGetRecordsTableAsync(
            host,
            code: "RR_SCHEMA_REPAIR_1",
            periodicity: ReferenceRegisterPeriodicity.Month,
            recordMode: ReferenceRegisterRecordMode.SubordinateToRecorder,
            CancellationToken.None);

        // Drop the table entirely.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.EnsureConnectionOpenAsync(CancellationToken.None);
            await uow.Connection.ExecuteAsync(
                $"DROP TABLE IF EXISTS {recordsTable} CASCADE;",
                transaction: uow.Transaction);
        }

        // Reports missing.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var endpoint = scope.ServiceProvider.GetRequiredService<IReferenceRegisterAdminEndpoint>();

            var health = await endpoint.GetPhysicalSchemaHealthByIdAsync(registerId, CancellationToken.None);
            health.Should().NotBeNull();
            health!.IsOk.Should().BeFalse();

            health.Records.TableName.Should().Be(recordsTable);
            health.Records.Exists.Should().BeFalse();
            health.Records.HasAppendOnlyGuard.Should().BeNull();
            health.Records.MissingColumns.Should().Contain("record_id");
            health.Records.MissingIndexes.Should().NotBeEmpty();
        }

        // Ensure repairs everything.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var endpoint = scope.ServiceProvider.GetRequiredService<IReferenceRegisterAdminEndpoint>();
            var repaired = await endpoint.EnsurePhysicalSchemaByIdAsync(registerId, CancellationToken.None);

            repaired.Should().NotBeNull();
            repaired!.IsOk.Should().BeTrue();
            repaired.Records.Exists.Should().BeTrue();
            repaired.Records.HasAppendOnlyGuard.Should().BeTrue();
            repaired.Records.MissingColumns.Should().BeEmpty();
            repaired.Records.MissingIndexes.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task GetPhysicalSchemaHealthById_WhenAppendOnlyGuardMissing_ReportsNotOk_AndEnsureRepairs()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (registerId, recordsTable) = await CreateRegisterAndGetRecordsTableAsync(
            host,
            code: "RR_SCHEMA_REPAIR_2",
            periodicity: ReferenceRegisterPeriodicity.Month,
            recordMode: ReferenceRegisterRecordMode.SubordinateToRecorder,
            CancellationToken.None);

        // Drop append-only guard trigger(s) from the records table.
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
                        WHERE t.tgrelid = '{recordsTable}'::regclass
                          AND NOT t.tgisinternal
                          AND p.proname = 'ngb_forbid_mutation_of_append_only_table'
                    LOOP
                        EXECUTE format('DROP TRIGGER %I ON %I', r.tgname, '{recordsTable}');
                    END LOOP;
                END $$;
                """,
                transaction: uow.Transaction);
        }

        // Reports missing guard.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var endpoint = scope.ServiceProvider.GetRequiredService<IReferenceRegisterAdminEndpoint>();

            var health = await endpoint.GetPhysicalSchemaHealthByIdAsync(registerId, CancellationToken.None);
            health.Should().NotBeNull();
            health!.IsOk.Should().BeFalse();

            health.Records.Exists.Should().BeTrue();
            health.Records.HasAppendOnlyGuard.Should().BeFalse();
            health.Records.IsOk.Should().BeFalse();
        }

        // Ensure repairs guard.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var endpoint = scope.ServiceProvider.GetRequiredService<IReferenceRegisterAdminEndpoint>();
            var repaired = await endpoint.EnsurePhysicalSchemaByIdAsync(registerId, CancellationToken.None);

            repaired.Should().NotBeNull();
            repaired!.IsOk.Should().BeTrue();
            repaired.Records.HasAppendOnlyGuard.Should().BeTrue();
        }
    }

    [Fact]
    public async Task GetPhysicalSchemaHealthById_WhenKeyIndexMissing_ReportsNotOk_AndEnsureRepairs()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (registerId, recordsTable) = await CreateRegisterAndGetRecordsTableAsync(
            host,
            code: "RR_SCHEMA_REPAIR_3",
            periodicity: ReferenceRegisterPeriodicity.Month,
            recordMode: ReferenceRegisterRecordMode.SubordinateToRecorder,
            CancellationToken.None);

        // Drop the v2 key index (deterministic hashed name).
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            await uow.Connection.ExecuteAsync(
                $"""
                DO $$
                DECLARE ix text;
                BEGIN
                    SELECT indexname INTO ix
                    FROM pg_indexes
                    WHERE schemaname = 'public'
                      AND tablename = '{recordsTable}'
                      AND indexname LIKE 'ix_refreg_key_v2_%'
                    LIMIT 1;

                    IF ix IS NOT NULL THEN
                        EXECUTE format('DROP INDEX %I', ix);
                    END IF;
                END $$;
                """,
                transaction: uow.Transaction);
        }

        // Reports missing index.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var endpoint = scope.ServiceProvider.GetRequiredService<IReferenceRegisterAdminEndpoint>();

            var health = await endpoint.GetPhysicalSchemaHealthByIdAsync(registerId, CancellationToken.None);
            health.Should().NotBeNull();
            health!.IsOk.Should().BeFalse();

            health.Records.Exists.Should().BeTrue();
            health.Records.MissingIndexes.Should().Contain(x =>
                x.Contains("dimension_set_id", StringComparison.OrdinalIgnoreCase)
                && x.Contains("recorder_document_id", StringComparison.OrdinalIgnoreCase));
        }

        // Ensure repairs indexes.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var endpoint = scope.ServiceProvider.GetRequiredService<IReferenceRegisterAdminEndpoint>();
            var repaired = await endpoint.EnsurePhysicalSchemaByIdAsync(registerId, CancellationToken.None);

            repaired.Should().NotBeNull();
            repaired!.IsOk.Should().BeTrue();
            repaired.Records.MissingIndexes.Should().BeEmpty();
        }
    }

    private static async Task<(Guid RegisterId, string RecordsTable)> CreateRegisterAndGetRecordsTableAsync(
        Microsoft.Extensions.Hosting.IHost host,
        string code,
        ReferenceRegisterPeriodicity periodicity,
        ReferenceRegisterRecordMode recordMode,
        CancellationToken ct)
    {
        await using var scope = host.Services.CreateAsyncScope();
        // NOTE: Management and schema ensure services manage their own UoW transactions.
        // Do NOT start an outer transaction here, otherwise nested transactions will fail.
        var mgmt = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();

        var id = await mgmt.UpsertAsync(
            code,
            name: "Schema Repair",
            periodicity,
            recordMode,
            ct);

        // Add at least one field to ensure the table contract includes dynamic columns.
        await mgmt.ReplaceFieldsAsync(
            id,
            new[]
            {
                new ReferenceRegisterFieldDefinition(
                    Code: "Amount",
                    Name: "Amount",
                    Ordinal: 10,
                    ColumnType: ColumnType.Decimal,
                    IsNullable: false)
            },
            ct);

        // Ensure physical table exists.
        var store = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsStore>();
        await store.EnsureSchemaAsync(id, ct);

        var repo = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRepository>();
        var reg = await repo.GetByIdAsync(id, ct);
        reg.Should().NotBeNull();

        var table = ReferenceRegisterNaming.RecordsTable(reg!.TableCode);
        return (id, table);
    }
}
