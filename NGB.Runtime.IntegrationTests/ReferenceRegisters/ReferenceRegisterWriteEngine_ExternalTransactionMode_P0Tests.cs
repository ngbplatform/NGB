using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Persistence.ReferenceRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.ReferenceRegisters;
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.ReferenceRegisters;

/// <summary>
/// P0: ReferenceRegisterWriteEngine supports external transaction mode (manageTransaction=false):
/// - fails fast when used without an active transaction
/// - respects the outer transaction commit/rollback semantics for BOTH the idempotency log and the write action
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ReferenceRegisterWriteEngine_ExternalTransactionMode_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string TxnRequired = "This operation requires an active transaction.";

    [Fact]
    public async Task ExecuteAsync_ManageTransactionFalse_WithoutActiveTransaction_Throws()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (registerId, _) = await ArrangeIndependentNonPeriodicRegisterAsync(host, "rr_we_ext_no_tx");
        var documentId = await ArrangeDocumentAsync(host, number: "RR-WE-EXT-NTX");

        await using var scope = host.Services.CreateAsyncScope();
        var engine = scope.ServiceProvider.GetRequiredService<IReferenceRegisterWriteEngine>();

        var called = false;
        Func<CancellationToken, Task> writeAction = _ =>
        {
            called = true;
            return Task.CompletedTask;
        };

        var act = () => engine.ExecuteAsync(
            registerId,
            documentId,
            ReferenceRegisterWriteOperation.Post,
            writeAction,
            manageTransaction: false,
            ct: CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>()
            .WithMessage(TxnRequired);

        called.Should().BeFalse("writeAction must not be invoked when external transaction mode is used without an active transaction");
    }

    [Fact]
    public async Task ExecuteAsync_ManageTransactionFalse_WhenOuterTransactionRollsBack_DoesNotPersistWriteLogOrRecords()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (registerId, recordsTable) = await ArrangeIndependentNonPeriodicRegisterAsync(host, "rr_we_ext_rb");
        var documentId = await ArrangeDocumentAsync(host, number: "RR-WE-EXT-RB");

        // Ensure the physical table exists OUTSIDE the external transaction we are about to rollback,
        // otherwise DDL could be rolled back along with the data changes.
        await EnsureRecordsSchemaAsync(host, registerId);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var engine = scope.ServiceProvider.GetRequiredService<IReferenceRegisterWriteEngine>();
            var store = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsStore>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await uow.BeginTransactionAsync(CancellationToken.None);
            try
            {
                var res = await engine.ExecuteAsync(
                    registerId,
                    documentId,
                    ReferenceRegisterWriteOperation.Post,
                    innerCt => store.AppendAsync(
                        registerId,
                        [new ReferenceRegisterRecordWrite(
                            DimensionSetId: Guid.Empty,
                            PeriodUtc: null,
                            RecorderDocumentId: null,
                            Values: new Dictionary<string, object?>(),
                            IsDeleted: false)],
                        innerCt),
                    manageTransaction: false,
                    ct: CancellationToken.None);

                res.Should().Be(ReferenceRegisterWriteResult.Executed);

                var logCountInTx = await uow.Connection.ExecuteScalarAsync<int>(
                    new CommandDefinition(
                        "SELECT COUNT(*) FROM reference_register_write_state WHERE register_id = @R AND document_id = @D AND operation = @O;",
                        new { R = registerId, D = documentId, O = (short)ReferenceRegisterWriteOperation.Post },
                        uow.Transaction,
                        cancellationToken: CancellationToken.None));

                logCountInTx.Should().Be(1);

                var completedAtInTx = await uow.Connection.ExecuteScalarAsync<DateTime?>(
                    new CommandDefinition(
                        "SELECT completed_at_utc FROM reference_register_write_state WHERE register_id = @R AND document_id = @D AND operation = @O;",
                        new { R = registerId, D = documentId, O = (short)ReferenceRegisterWriteOperation.Post },
                        uow.Transaction,
                        cancellationToken: CancellationToken.None));

                completedAtInTx.Should().NotBeNull();

                var recordsInTx = await uow.Connection.ExecuteScalarAsync<int>(
                    new CommandDefinition(
                        $"SELECT COUNT(*) FROM {recordsTable};",
                        transaction: uow.Transaction,
                        cancellationToken: CancellationToken.None));

                recordsInTx.Should().Be(1);
            }
            finally
            {
                await uow.RollbackAsync(CancellationToken.None);
            }
        }

        // Assert: nothing persisted.
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);

            var logCount = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM reference_register_write_state WHERE register_id = @R AND document_id = @D AND operation = @O;",
                new { R = registerId, D = documentId, O = (short)ReferenceRegisterWriteOperation.Post });

            logCount.Should().Be(0);

            var records = await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {recordsTable};");
            records.Should().Be(0);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ManageTransactionFalse_WhenOuterTransactionCommits_PersistsWriteLogAndRecords()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (registerId, recordsTable) = await ArrangeIndependentNonPeriodicRegisterAsync(host, "rr_we_ext_commit");
        var documentId = await ArrangeDocumentAsync(host, number: "RR-WE-EXT-COMMIT");

        await EnsureRecordsSchemaAsync(host, registerId);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var engine = scope.ServiceProvider.GetRequiredService<IReferenceRegisterWriteEngine>();
            var store = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsStore>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            var res = await engine.ExecuteAsync(
                registerId,
                documentId,
                ReferenceRegisterWriteOperation.Post,
                innerCt => store.AppendAsync(
                    registerId,
                    [new ReferenceRegisterRecordWrite(
                        DimensionSetId: Guid.Empty,
                        PeriodUtc: null,
                        RecorderDocumentId: null,
                        Values: new Dictionary<string, object?>(),
                        IsDeleted: false)],
                    innerCt),
                manageTransaction: false,
                ct: CancellationToken.None);

            res.Should().Be(ReferenceRegisterWriteResult.Executed);

            await uow.CommitAsync(CancellationToken.None);
        }

        // Assert: committed state is visible outside.
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);

            var completedAt = await conn.ExecuteScalarAsync<DateTime?>(
                "SELECT completed_at_utc FROM reference_register_write_state WHERE register_id = @R AND document_id = @D AND operation = @O;",
                new { R = registerId, D = documentId, O = (short)ReferenceRegisterWriteOperation.Post });

            completedAt.Should().NotBeNull("external commit must persist the completed log row");

            var records = await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {recordsTable};");
            records.Should().Be(1);
        }
    }

    private static async Task<(Guid RegisterId, string RecordsTable)> ArrangeIndependentNonPeriodicRegisterAsync(IHost host, string code)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var mgmt = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();
        var repo = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRepository>();

        var registerId = await mgmt.UpsertAsync(
            code: code,
            name: $"{code} name",
            periodicity: ReferenceRegisterPeriodicity.NonPeriodic,
            recordMode: ReferenceRegisterRecordMode.Independent,
            ct: CancellationToken.None);

        // Keep the schema minimal: no dimension rules and no fields.
        await mgmt.ReplaceDimensionRulesAsync(registerId, [], CancellationToken.None);
        await mgmt.ReplaceFieldsAsync(registerId, [], CancellationToken.None);

        var reg = await repo.GetByIdAsync(registerId, CancellationToken.None);
        reg.Should().NotBeNull();

        var table = ReferenceRegisterNaming.RecordsTable(reg!.TableCode);
        return (registerId, table);
    }

    private static async Task EnsureRecordsSchemaAsync(IHost host, Guid registerId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsStore>();
        await store.EnsureSchemaAsync(registerId, CancellationToken.None);
    }

    private static async Task<Guid> ArrangeDocumentAsync(IHost host, string number)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();

        return await drafts.CreateDraftAsync(
            typeCode: "test_doc",
            number: number,
            dateUtc: new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc),
            manageTransaction: true,
            ct: CancellationToken.None);
    }
}
