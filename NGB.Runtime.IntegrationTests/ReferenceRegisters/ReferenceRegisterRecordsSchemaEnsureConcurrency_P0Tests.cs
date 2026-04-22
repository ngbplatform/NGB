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
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.ReferenceRegisters;

/// <summary>
/// P0: Reference register records store uses dynamic DDL + CREATE TRIGGER DO-block (no IF NOT EXISTS).
/// EnsureSchemaAsync must be serialized per register to avoid races when multiple writers hit a new register concurrently.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ReferenceRegisterRecordsSchemaEnsureConcurrency_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task EnsureSchema_CreatesRecordsTable_Once_UnderConcurrency()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        const string code = "RR_TEST";
        var registerId = Guid.Empty;

        // Arrange: create register with some metadata to make EnsureSchema slower and more race-prone.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();

            registerId = await svc.UpsertAsync(
                code,
                name: "Reference Register Test",
                periodicity: ReferenceRegisterPeriodicity.Day,
                recordMode: ReferenceRegisterRecordMode.Independent,
                ct: CancellationToken.None);

            await svc.ReplaceDimensionRulesAsync(
                registerId,
                [
                    new ReferenceRegisterDimensionRule(
                        DimensionId: DeterministicGuid.Create("Dimension|building"),
                        DimensionCode: "building",
                        Ordinal: 10,
                        IsRequired: true),
                ],
                ct: CancellationToken.None);

            await svc.ReplaceFieldsAsync(
                registerId,
                fields:
                [
                    new ReferenceRegisterFieldDefinition("amount", "Amount", 10, ColumnType.Decimal, true),
                    new ReferenceRegisterFieldDefinition("note", "Note", 20, ColumnType.String, true),
                    new ReferenceRegisterFieldDefinition("qty", "Qty", 30, ColumnType.Int32, true),
                ],
                ct: CancellationToken.None);
        }

        // Act: hit EnsureSchema concurrently in separate transactions/scopes.
        const int workers = 6;

        var allReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = 0;

        async Task Worker()
        {
            await using var scope = host.Services.CreateAsyncScope();

            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var store = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsStore>();

            if (Interlocked.Increment(ref ready) == workers)
                allReady.TrySetResult();

            await allReady.Task;
            await go.Task;

            await uow.BeginTransactionAsync(CancellationToken.None);
            await store.EnsureSchemaAsync(registerId, CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
        }

        var tasks = Enumerable.Range(0, workers).Select(_ => Worker()).ToArray();

        await allReady.Task;
        go.TrySetResult();

        Func<Task> act = () => Task.WhenAll(tasks);
        await act.Should().NotThrowAsync();

        // Assert: table exists and has exactly one append-only trigger.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            var tableCode = ReferenceRegisterNaming.NormalizeTableCode(code);
            var table = $"refreg_{tableCode}__records";

            // IMPORTANT: avoid returning regclass from SQL, Npgsql can't read regclass as object.
            var exists = await uow.Connection.ExecuteScalarAsync<bool>(
                new CommandDefinition(
                    "SELECT to_regclass(@Table) IS NOT NULL;",
                    new { Table = table },
                    cancellationToken: CancellationToken.None));

            exists.Should().BeTrue();

            var trgCount = await uow.Connection.ExecuteScalarAsync<int>(
                new CommandDefinition(
                    """
                    SELECT COUNT(*)
                    FROM pg_trigger
                    WHERE tgrelid = @Table::regclass
                      AND NOT tgisinternal
                      AND tgname LIKE 'trg_refreg_append_only_%';
                    """,
                    new { Table = table },
                    cancellationToken: CancellationToken.None));

            trgCount.Should().Be(1, "EnsureSchema must create the append-only trigger exactly once even under concurrency");
        }
    }
}
