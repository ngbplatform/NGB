using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.PostingState;
using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.Persistence.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisterWriteLog_History_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private sealed class HistoryRow
    {
        public Guid HistoryId { get; init; }
        public short EventKind { get; init; }
        public DateTime OccurredAtUtc { get; init; }
    }

    [Fact]
    public async Task TryBegin_WhenStaleTakeover_HistoryCapturesSupersededAndNewAttempt()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var regRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var log = scope.ServiceProvider.GetRequiredService<IOperationalRegisterWriteStateRepository>();

        var regId = Guid.CreateVersion7();
        var docId = Guid.CreateVersion7();
        var nowUtc = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);
        var staleStartedAtUtc = nowUtc.AddMinutes(-11);

        await uow.BeginTransactionAsync(CancellationToken.None);
        await SeedRegisterAndDocumentAsync(regRepo, docs, regId, docId, nowUtc);

        (await log.TryBeginAsync(regId, docId, OperationalRegisterWriteOperation.Post, staleStartedAtUtc, CancellationToken.None))
            .Should().Be(PostingStateBeginResult.Begun);

        await uow.CommitAsync(CancellationToken.None);

        await uow.BeginTransactionAsync(CancellationToken.None);

        (await log.TryBeginAsync(regId, docId, OperationalRegisterWriteOperation.Post, nowUtc, CancellationToken.None))
            .Should().Be(PostingStateBeginResult.Begun);

        var history = (await uow.Connection.QueryAsync<HistoryRow>(
            new CommandDefinition(
                """
                SELECT history_id AS HistoryId, event_kind AS EventKind, occurred_at_utc AS OccurredAtUtc
                FROM operational_register_write_log_history
                WHERE register_id = @RegisterId
                  AND document_id = @DocumentId
                  AND operation = @Operation
                ORDER BY occurred_at_utc, history_id;
                """,
                new
                {
                    RegisterId = regId,
                    DocumentId = docId,
                    Operation = (short)OperationalRegisterWriteOperation.Post
                },
                transaction: uow.Transaction,
                cancellationToken: CancellationToken.None))).ToArray();

        history.Should().HaveCount(3);
        history[0].EventKind.Should().Be((short)1);
        history.Skip(1).Select(x => x.EventKind).Should().BeEquivalentTo(new[] { (short)3, (short)1 });

        await uow.CommitAsync(CancellationToken.None);
    }

    private static async Task SeedRegisterAndDocumentAsync(
        IOperationalRegisterRepository regRepo,
        IDocumentRepository docs,
        Guid registerId,
        Guid documentId,
        DateTime nowUtc)
    {
        await regRepo.UpsertAsync(new OperationalRegisterUpsert(registerId, "RR", "Rent Roll"), nowUtc, CancellationToken.None);

        await docs.CreateAsync(new DocumentRecord
        {
            Id = documentId,
            TypeCode = "it_doc",
            Number = null,
            DateUtc = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
            Status = DocumentStatus.Draft,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            PostedAtUtc = null,
            MarkedForDeletionAtUtc = null
        }, CancellationToken.None);
    }
}
