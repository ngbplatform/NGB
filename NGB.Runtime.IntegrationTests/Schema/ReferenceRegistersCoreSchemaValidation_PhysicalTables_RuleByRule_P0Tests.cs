using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.ReferenceRegisters;
using NGB.Persistence.Schema;
using NGB.Persistence.UnitOfWork;
using NGB.ReferenceRegisters;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.ReferenceRegisters;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Schema;

/// <summary>
/// P0: Reference registers core schema validation must detect physical per-register table drift
/// (append-only guard / per-register indexes / semantic constraints) for registers with has_records=true,
/// and the per-register EnsureSchema path must repair the drift.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ReferenceRegistersCoreSchemaValidation_PhysicalTables_RuleByRule_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ValidateAsync_WhenRecordsAppendOnlyGuardMissing_Fails_ThenEnsureSchemaRepairs()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (registerId, table) = await CreateIndependentMonthlyRegisterWithRecordsAsync(host, code: "RR_CORE_PHYS_1", ct: CancellationToken.None);

        await DropTriggersByPrefixAsync(host, table, "trg_refreg_append_only_", CancellationToken.None);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var core = scope.ServiceProvider.GetRequiredService<IReferenceRegistersCoreSchemaValidationService>();

            Func<Task> act = () => core.ValidateAsync(CancellationToken.None);

            await act.Should().ThrowAsync<NgbConfigurationViolationException>()
                .WithMessage($"*Table '{table}' is missing append-only trigger (ngb_forbid_mutation_of_append_only_table).*");
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsStore>();
            await store.EnsureSchemaAsync(registerId, CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var core = scope.ServiceProvider.GetRequiredService<IReferenceRegistersCoreSchemaValidationService>();
            Func<Task> act = () => core.ValidateAsync(CancellationToken.None);
            await act.Should().NotThrowAsync();
        }
    }

    [Fact]
    public async Task ValidateAsync_WhenKeyV2IndexMissing_Fails_ThenEnsureSchemaRepairs()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (registerId, table) = await CreateIndependentMonthlyRegisterWithRecordsAsync(host, code: "RR_CORE_PHYS_2", ct: CancellationToken.None);

        await DropIndexesByPrefixAsync(host, table, "ix_refreg_key_v2_", CancellationToken.None);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var core = scope.ServiceProvider.GetRequiredService<IReferenceRegistersCoreSchemaValidationService>();

            Func<Task> act = () => core.ValidateAsync(CancellationToken.None);

            // Monthly register => key_v2 index (periodic) required.
            await act.Should().ThrowAsync<NgbConfigurationViolationException>()
                .WithMessage($"*Table '{table}' is missing key_v2 index (periodic): expected index on (dimension_set_id, recorder_document_id, period_bucket_utc, period_utc, recorded_at_utc, record_id).*");
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsStore>();
            await store.EnsureSchemaAsync(registerId, CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var core = scope.ServiceProvider.GetRequiredService<IReferenceRegistersCoreSchemaValidationService>();
            Func<Task> act = () => core.ValidateAsync(CancellationToken.None);
            await act.Should().NotThrowAsync();
        }
    }

    [Fact]
    public async Task ValidateAsync_WhenSemanticConstraintsMissingForIndependentNonPeriodic_Fails_ThenEnsureSchemaRepairs()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (registerId, table) = await CreateIndependentNonPeriodicRegisterWithRecordsAsync(host, code: "RR_CORE_PHYS_3", ct: CancellationToken.None);

        await DropConstraintsByPrefixAsync(host, table, "ck_refreg_recorder_null_", CancellationToken.None);
        await DropConstraintsByPrefixAsync(host, table, "ck_refreg_nonperiodic_", CancellationToken.None);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var core = scope.ServiceProvider.GetRequiredService<IReferenceRegistersCoreSchemaValidationService>();

            Func<Task> act = () => core.ValidateAsync(CancellationToken.None);

            var ex = await act.Should().ThrowAsync<NgbConfigurationViolationException>();
            ex.Which.Message.Should().Contain(
                    $"Table '{table}' is missing semantic CHECK constraint enforcing recorder_document_id IS NULL (Independent register).")
                .And.Contain(
                    $"Table '{table}' is missing semantic CHECK constraint enforcing period_utc IS NULL AND period_bucket_utc IS NULL (NonPeriodic register).");
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsStore>();
            await store.EnsureSchemaAsync(registerId, CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var core = scope.ServiceProvider.GetRequiredService<IReferenceRegistersCoreSchemaValidationService>();
            Func<Task> act = () => core.ValidateAsync(CancellationToken.None);
            await act.Should().NotThrowAsync();
        }
    }

    private static async Task<(Guid RegisterId, string Table)> CreateIndependentMonthlyRegisterWithRecordsAsync(
        Microsoft.Extensions.Hosting.IHost host,
        string code,
        CancellationToken ct)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var mgmt = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();
        var registerId = await mgmt.UpsertAsync(
            code,
            name: "RR Core Phys (Month)",
            periodicity: ReferenceRegisterPeriodicity.Month,
            recordMode: ReferenceRegisterRecordMode.Independent,
            ct);

        // Ensure physical schema, then write at least one record (has_records=true path).
        var store = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsStore>();
        await store.EnsureSchemaAsync(registerId, ct);

        var writer = scope.ServiceProvider.GetRequiredService<IReferenceRegisterIndependentWriteService>();
        var res = await writer.UpsertByDimensionSetIdAsync(
            registerId,
            dimensionSetId: Guid.Empty,
            periodUtc: new DateTime(2026, 02, 01, 12, 0, 0, DateTimeKind.Utc),
            values: new Dictionary<string, object?>(),
            commandId: Guid.CreateVersion7(),
            manageTransaction: true,
            ct);

        res.Should().Be(ReferenceRegisterWriteResult.Executed);

        var repo = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRepository>();
        var reg = await repo.GetByIdAsync(registerId, ct);
        reg.Should().NotBeNull();

        var table = ReferenceRegisterNaming.RecordsTable(reg!.TableCode);
        return (registerId, table);
    }

    private static async Task<(Guid RegisterId, string Table)> CreateIndependentNonPeriodicRegisterWithRecordsAsync(
        Microsoft.Extensions.Hosting.IHost host,
        string code,
        CancellationToken ct)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var mgmt = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();
        var registerId = await mgmt.UpsertAsync(
            code,
            name: "RR Core Phys (NonPeriodic)",
            periodicity: ReferenceRegisterPeriodicity.NonPeriodic,
            recordMode: ReferenceRegisterRecordMode.Independent,
            ct);

        var store = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsStore>();
        await store.EnsureSchemaAsync(registerId, ct);

        var writer = scope.ServiceProvider.GetRequiredService<IReferenceRegisterIndependentWriteService>();
        var res = await writer.UpsertByDimensionSetIdAsync(
            registerId,
            dimensionSetId: Guid.Empty,
            periodUtc: null,
            values: new Dictionary<string, object?>(),
            commandId: Guid.CreateVersion7(),
            manageTransaction: true,
            ct);

        res.Should().Be(ReferenceRegisterWriteResult.Executed);

        var repo = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRepository>();
        var reg = await repo.GetByIdAsync(registerId, ct);
        reg.Should().NotBeNull();

        var table = ReferenceRegisterNaming.RecordsTable(reg!.TableCode);
        return (registerId, table);
    }

    private static async Task DropIndexesByPrefixAsync(
        Microsoft.Extensions.Hosting.IHost host,
        string table,
        string indexPrefix,
        CancellationToken ct)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow.EnsureConnectionOpenAsync(ct);

        var indexes = (await uow.Connection.QueryAsync<string>(
                new CommandDefinition(
                    """
                    SELECT indexname
                    FROM pg_indexes
                    WHERE schemaname = 'public'
                      AND tablename = @Table
                      AND indexname LIKE @Prefix;
                    """,
                    new { Table = table, Prefix = indexPrefix + "%" },
                    transaction: uow.Transaction,
                    cancellationToken: ct)))
            .ToList();

        foreach (var ix in indexes)
        {
            await uow.Connection.ExecuteAsync(
                new CommandDefinition($"DROP INDEX IF EXISTS {ix};", transaction: uow.Transaction, cancellationToken: ct));
        }
    }

    private static async Task DropTriggersByPrefixAsync(
        Microsoft.Extensions.Hosting.IHost host,
        string table,
        string triggerPrefix,
        CancellationToken ct)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow.EnsureConnectionOpenAsync(ct);

        var triggers = (await uow.Connection.QueryAsync<string>(
                new CommandDefinition(
                    """
                    SELECT t.tgname
                    FROM pg_trigger t
                    JOIN pg_class cl ON cl.oid = t.tgrelid
                    JOIN pg_namespace ns ON ns.oid = cl.relnamespace
                    WHERE ns.nspname = 'public'
                      AND cl.relname = @Table
                      AND NOT t.tgisinternal
                      AND t.tgname LIKE @Prefix;
                    """,
                    new { Table = table, Prefix = triggerPrefix + "%" },
                    transaction: uow.Transaction,
                    cancellationToken: ct)))
            .ToList();

        foreach (var trg in triggers)
        {
            await uow.Connection.ExecuteAsync(
                new CommandDefinition($"DROP TRIGGER IF EXISTS {trg} ON {table};", transaction: uow.Transaction, cancellationToken: ct));
        }
    }

    private static async Task DropConstraintsByPrefixAsync(
        Microsoft.Extensions.Hosting.IHost host,
        string table,
        string constraintPrefix,
        CancellationToken ct)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow.EnsureConnectionOpenAsync(ct);

        var constraints = (await uow.Connection.QueryAsync<string>(
                new CommandDefinition(
                    """
                    SELECT c.conname
                    FROM pg_constraint c
                    JOIN pg_class cl ON cl.oid = c.conrelid
                    JOIN pg_namespace ns ON ns.oid = cl.relnamespace
                    WHERE ns.nspname = 'public'
                      AND cl.relname = @Table
                      AND c.contype = 'c'
                      AND c.conname LIKE @Prefix;
                    """,
                    new { Table = table, Prefix = constraintPrefix + "%" },
                    transaction: uow.Transaction,
                    cancellationToken: ct)))
            .ToList();

        foreach (var ck in constraints)
        {
            await uow.Connection.ExecuteAsync(
                new CommandDefinition($"ALTER TABLE {table} DROP CONSTRAINT IF EXISTS {ck};", transaction: uow.Transaction, cancellationToken: ct));
        }
    }
}
