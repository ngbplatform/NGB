using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.PostingState;
using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.Persistence.PostingState;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Posting;

[Collection(PostgresCollection.Name)]
public sealed class PostingLog_History_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const short StartedEvent = 1;
    private const short CompletedEvent = 2;

    private sealed class HistoryRow
    {
        public short EventKind { get; init; }
        public DateTime OccurredAtUtc { get; init; }
    }

    private sealed class StateRow
    {
        public DateTime StartedAtUtc { get; init; }
        public DateTime? CompletedAtUtc { get; init; }
    }

    [Fact]
    public async Task ClearCompletedStateAsync_KeepsImmutableHistory()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var log = scope.ServiceProvider.GetRequiredService<IPostingStateRepository>();

        var docId = Guid.CreateVersion7();
        var nowUtc = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

        await uow.BeginTransactionAsync(CancellationToken.None);
        await SeedDocumentAsync(docs, docId, nowUtc);

        (await log.TryBeginAsync(docId, PostingOperation.Post, nowUtc, CancellationToken.None))
            .Should().Be(PostingStateBeginResult.Begun);

        await log.MarkCompletedAsync(docId, PostingOperation.Post, nowUtc.AddSeconds(1), CancellationToken.None);
        await log.ClearCompletedStateAsync(docId, PostingOperation.Post, CancellationToken.None);

        var history = (await uow.Connection.QueryAsync<HistoryRow>(
            new CommandDefinition(
                """
                SELECT event_kind AS EventKind, occurred_at_utc AS OccurredAtUtc
                FROM accounting_posting_log_history
                WHERE document_id = @DocumentId
                  AND operation = @Operation
                ORDER BY occurred_at_utc, event_kind;
                """,
                new { DocumentId = docId, Operation = (short)PostingOperation.Post },
                transaction: uow.Transaction,
                cancellationToken: CancellationToken.None))).ToArray();

        history.Select(x => x.EventKind).Should().Equal(StartedEvent, CompletedEvent);

        var stateRows = await uow.Connection.ExecuteScalarAsync<int>(
            new CommandDefinition(
                "SELECT COUNT(*) FROM accounting_posting_state WHERE document_id = @DocumentId AND operation = @Operation;",
                new { DocumentId = docId, Operation = (short)PostingOperation.Post },
                transaction: uow.Transaction,
                cancellationToken: CancellationToken.None));

        stateRows.Should().Be(0);

        await uow.CommitAsync(CancellationToken.None);
    }

    [Fact]
    public async Task MarkCompleted_WhenCompletedTimePrecedesStarted_ClampsPostingStateAndHistoryToStartedAt()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var log = scope.ServiceProvider.GetRequiredService<IPostingStateRepository>();

        var docId = Guid.CreateVersion7();
        var startedAtUtc = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);
        var completedAtUtc = startedAtUtc.AddSeconds(-30);

        await uow.BeginTransactionAsync(CancellationToken.None);
        await SeedDocumentAsync(docs, docId, startedAtUtc);

        (await log.TryBeginAsync(docId, PostingOperation.Post, startedAtUtc, CancellationToken.None))
            .Should().Be(PostingStateBeginResult.Begun);

        await log.MarkCompletedAsync(docId, PostingOperation.Post, completedAtUtc, CancellationToken.None);

        var state = await ReadStateAsync(uow, docId, PostingOperation.Post);
        state.Should().NotBeNull();
        state!.StartedAtUtc.Should().Be(startedAtUtc);
        state.CompletedAtUtc.Should().Be(startedAtUtc);

        var history = await ReadHistoryAsync(uow, docId, PostingOperation.Post);
        history.Select(x => x.EventKind).Should().Equal(StartedEvent, CompletedEvent);
        history.Single(x => x.EventKind == StartedEvent).OccurredAtUtc.Should().Be(startedAtUtc);
        history.Single(x => x.EventKind == CompletedEvent).OccurredAtUtc.Should().Be(startedAtUtc);

        await uow.CommitAsync(CancellationToken.None);
    }

    [Fact]
    public async Task MarkCompleted_WhenAlreadyCompleted_DoesNotAppendDuplicateCompletedHistory()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var log = scope.ServiceProvider.GetRequiredService<IPostingStateRepository>();

        var docId = Guid.CreateVersion7();
        var startedAtUtc = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);
        var completedAtUtc = startedAtUtc.AddSeconds(5);
        var secondCompletedAtUtc = startedAtUtc.AddMinutes(1);

        await uow.BeginTransactionAsync(CancellationToken.None);
        await SeedDocumentAsync(docs, docId, startedAtUtc);

        (await log.TryBeginAsync(docId, PostingOperation.Post, startedAtUtc, CancellationToken.None))
            .Should().Be(PostingStateBeginResult.Begun);

        await log.MarkCompletedAsync(docId, PostingOperation.Post, completedAtUtc, CancellationToken.None);
        await log.MarkCompletedAsync(docId, PostingOperation.Post, secondCompletedAtUtc, CancellationToken.None);

        var state = await ReadStateAsync(uow, docId, PostingOperation.Post);
        state.Should().NotBeNull();
        state!.CompletedAtUtc.Should().Be(completedAtUtc);

        var history = await ReadHistoryAsync(uow, docId, PostingOperation.Post);
        history.Count(x => x.EventKind == StartedEvent).Should().Be(1);
        history.Count(x => x.EventKind == CompletedEvent).Should().Be(1);
        history.Single(x => x.EventKind == CompletedEvent).OccurredAtUtc.Should().Be(completedAtUtc);

        await uow.CommitAsync(CancellationToken.None);
    }

    private static Task SeedDocumentAsync(IDocumentRepository docs, Guid documentId, DateTime nowUtc)
        => docs.CreateAsync(new DocumentRecord
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

    private static Task<StateRow?> ReadStateAsync(
        IUnitOfWork uow,
        Guid documentId,
        PostingOperation operation)
        => uow.Connection.QuerySingleOrDefaultAsync<StateRow>(
            new CommandDefinition(
                """
                SELECT started_at_utc AS StartedAtUtc,
                       completed_at_utc AS CompletedAtUtc
                FROM accounting_posting_state
                WHERE document_id = @DocumentId
                  AND operation = @Operation;
                """,
                new { DocumentId = documentId, Operation = (short)operation },
                transaction: uow.Transaction,
                cancellationToken: CancellationToken.None));

    private static async Task<IReadOnlyList<HistoryRow>> ReadHistoryAsync(
        IUnitOfWork uow,
        Guid documentId,
        PostingOperation operation)
        => (await uow.Connection.QueryAsync<HistoryRow>(
            new CommandDefinition(
                """
                SELECT event_kind AS EventKind,
                       occurred_at_utc AS OccurredAtUtc
                FROM accounting_posting_log_history
                WHERE document_id = @DocumentId
                  AND operation = @Operation
                ORDER BY occurred_at_utc, event_kind;
                """,
                new { DocumentId = documentId, Operation = (short)operation },
                transaction: uow.Transaction,
                cancellationToken: CancellationToken.None))).ToArray();
}
