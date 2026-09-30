using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.PostingState;
using NGB.Persistence.ReferenceRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.ReferenceRegisters;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.ReferenceRegisters;

[Collection(RegistersPostgresCollection.Name)]
public sealed class ReferenceRegisterWriteStateBatchRepository_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Batch_begin_and_complete_preserve_state_and_history_for_every_register()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var registerIds = new[]
        {
            await ArrangeRegisterAsync(host, "rr.batch.a"),
            await ArrangeRegisterAsync(host, "rr.batch.b")
        };
        var documentId = await ArrangeDraftDocumentAsync(host);
        var startedAtUtc = DateTime.UtcNow;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IReferenceRegisterWriteStateRepository>();
            var batch = repository.Should().BeAssignableTo<IReferenceRegisterWriteStateBatchRepository>().Subject;
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await uow.ExecuteInUowTransactionAsync(async ct =>
            {
                var results = await batch.TryBeginManyAsync(
                    [registerIds[1], registerIds[0], registerIds[1]],
                    documentId,
                    ReferenceRegisterWriteOperation.Post,
                    startedAtUtc,
                    ct);

                results.Should().HaveCount(2);
                results.Values.Should().OnlyContain(result => result == PostingStateBeginResult.Begun);

                await batch.MarkCompletedManyAsync(
                    registerIds,
                    documentId,
                    ReferenceRegisterWriteOperation.Post,
                    startedAtUtc.AddSeconds(-1),
                    ct);
            });

            await uow.ExecuteInUowTransactionAsync(async ct =>
            {
                var results = await batch.TryBeginManyAsync(
                    registerIds,
                    documentId,
                    ReferenceRegisterWriteOperation.Post,
                    startedAtUtc.AddMinutes(1),
                    ct);

                results.Values.Should().OnlyContain(
                    result => result == PostingStateBeginResult.AlreadyCompleted);
            });
        }

        await using var connection = new NpgsqlConnection(Fixture.ConnectionString);
        await connection.OpenAsync();

        var states = await connection.QueryAsync<(Guid RegisterId, DateTime StartedAtUtc, DateTime? CompletedAtUtc)>(
            """
            SELECT
                register_id AS "RegisterId",
                started_at_utc AS "StartedAtUtc",
                completed_at_utc AS "CompletedAtUtc"
            FROM reference_register_write_state
            WHERE document_id = @DocumentId
              AND operation = @Operation
            ORDER BY register_id;
            """,
            new { DocumentId = documentId, Operation = (short)ReferenceRegisterWriteOperation.Post });
        var history = await connection.QueryAsync<(Guid RegisterId, short EventKind, DateTime OccurredAtUtc)>(
            """
            SELECT
                register_id AS "RegisterId",
                event_kind AS "EventKind",
                occurred_at_utc AS "OccurredAtUtc"
            FROM reference_register_write_log_history
            WHERE document_id = @DocumentId
              AND operation = @Operation
            ORDER BY register_id, event_kind;
            """,
            new { DocumentId = documentId, Operation = (short)ReferenceRegisterWriteOperation.Post });

        states.Should().HaveCount(2);
        states.Should().OnlyContain(state =>
            state.CompletedAtUtc == state.StartedAtUtc && registerIds.Contains(state.RegisterId));
        history.Should().HaveCount(4);
        history.GroupBy(row => row.RegisterId).Should().OnlyContain(group =>
            group.Select(row => row.EventKind).SequenceEqual(new short[] { 1, 2 }));
    }

    private static async Task<Guid> ArrangeRegisterAsync(Microsoft.Extensions.Hosting.IHost host, string code)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var management = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();
        return await management.UpsertAsync(
            code,
            $"{code} name",
            ReferenceRegisterPeriodicity.NonPeriodic,
            ReferenceRegisterRecordMode.Independent,
            CancellationToken.None);
    }

    private static async Task<Guid> ArrangeDraftDocumentAsync(Microsoft.Extensions.Hosting.IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IDocumentDraftService>().CreateDraftAsync(
            typeCode: "test_doc",
            number: null,
            dateUtc: DateTime.UtcNow,
            manageTransaction: true,
            ct: CancellationToken.None);
    }
}
