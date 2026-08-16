using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using NGB.Accounting.PostingState;
using NGB.Persistence.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(DocumentsPostgresCollection.Name)]
public sealed class DocumentOperationState_History_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const short StartedEvent = 1;
    private const short CompletedEvent = 2;
    private const short SupersededEvent = 3;

    [Fact]
    public async Task TryBeginAndMarkCompleted_WritesStartedAndCompletedHistory()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var documentId = Guid.CreateVersion7();
        await InsertDraftDocumentAsync(Fixture.ConnectionString, documentId, "test.doc.history");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            var uow = sp.GetRequiredService<IUnitOfWork>();
            var repo = sp.GetRequiredService<IDocumentOperationStateRepository>();

            await uow.BeginTransactionAsync(CancellationToken.None);
            try
            {
                var begin = await repo.TryBeginAsync(documentId, PostingOperation.Post, DateTime.UtcNow, CancellationToken.None);
                begin.Should().Be(PostingStateBeginResult.Begun);

                await repo.MarkCompletedAsync(documentId, PostingOperation.Post, DateTime.UtcNow, CancellationToken.None);
                await uow.CommitAsync(CancellationToken.None);
            }
            catch
            {
                await uow.RollbackAsync(CancellationToken.None);
                throw;
            }
        }

        var events = await ReadHistoryEventsAsync(Fixture.ConnectionString, documentId, PostingOperation.Post);
        events.Should().ContainInOrder(StartedEvent, CompletedEvent);
    }

    [Fact]
    public async Task TryBegin_WhenStateIsStale_AppendsSupersededAndNewStartedHistory()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var documentId = Guid.CreateVersion7();
        await InsertDraftDocumentAsync(Fixture.ConnectionString, documentId, "test.doc.history.stale");

        var oldAttemptId = Guid.CreateVersion7();
        await InsertStaleInProgressStateAsync(
            Fixture.ConnectionString,
            documentId,
            PostingOperation.Unpost,
            oldAttemptId,
            DateTime.UtcNow.AddHours(-2));

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            var uow = sp.GetRequiredService<IUnitOfWork>();
            var repo = sp.GetRequiredService<IDocumentOperationStateRepository>();

            await uow.BeginTransactionAsync(CancellationToken.None);
            try
            {
                var begin = await repo.TryBeginAsync(documentId, PostingOperation.Unpost, DateTime.UtcNow, CancellationToken.None);
                begin.Should().Be(PostingStateBeginResult.Begun);
                await uow.CommitAsync(CancellationToken.None);
            }
            catch
            {
                await uow.RollbackAsync(CancellationToken.None);
                throw;
            }
        }

        var events = await ReadHistoryEventsAsync(Fixture.ConnectionString, documentId, PostingOperation.Unpost);
        events.Should().Contain(SupersededEvent);
        events.Should().Contain(StartedEvent);
    }

    [Fact]
    public async Task MarkCompleted_WhenCompletedTimePrecedesStarted_ClampsStateAndCompletedHistoryToStartedAt()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var documentId = Guid.CreateVersion7();
        var operation = PostingOperation.Post;
        var startedAtUtc = new DateTime(2026, 3, 7, 12, 0, 0, DateTimeKind.Utc);
        var completedAtUtc = startedAtUtc.AddSeconds(-15);

        await InsertDraftDocumentAsync(Fixture.ConnectionString, documentId, "test.doc.history.clock_skew");
        await BeginAndCompleteAsync(host, documentId, operation, startedAtUtc, completedAtUtc);

        var state = await ReadStateAsync(Fixture.ConnectionString, documentId, operation);
        state.Should().NotBeNull();
        state!.StartedAtUtc.Should().Be(startedAtUtc);
        state.CompletedAtUtc.Should().Be(startedAtUtc);

        var events = await ReadHistoryEventRowsAsync(Fixture.ConnectionString, documentId, operation);
        events.Select(x => x.EventKind).Should().ContainInOrder(StartedEvent, CompletedEvent);
        events.Single(x => x.EventKind == StartedEvent).OccurredAtUtc.Should().Be(startedAtUtc);
        events.Single(x => x.EventKind == CompletedEvent).OccurredAtUtc.Should().Be(startedAtUtc);
    }

    [Fact]
    public async Task MarkCompleted_WhenCompletedTimeIsAfterStarted_UsesProvidedCompletedTimeForStateAndHistory()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var documentId = Guid.CreateVersion7();
        var operation = PostingOperation.Repost;
        var startedAtUtc = new DateTime(2026, 3, 7, 12, 0, 0, DateTimeKind.Utc);
        var completedAtUtc = startedAtUtc.AddSeconds(42);

        await InsertDraftDocumentAsync(Fixture.ConnectionString, documentId, "test.doc.history.normal_completion");
        await BeginAndCompleteAsync(host, documentId, operation, startedAtUtc, completedAtUtc);

        var state = await ReadStateAsync(Fixture.ConnectionString, documentId, operation);
        state.Should().NotBeNull();
        state!.StartedAtUtc.Should().Be(startedAtUtc);
        state.CompletedAtUtc.Should().Be(completedAtUtc);

        var events = await ReadHistoryEventRowsAsync(Fixture.ConnectionString, documentId, operation);
        events.Select(x => x.EventKind).Should().ContainInOrder(StartedEvent, CompletedEvent);
        events.Single(x => x.EventKind == CompletedEvent).OccurredAtUtc.Should().Be(completedAtUtc);
    }

    [Fact]
    public async Task MarkCompleted_WhenAlreadyCompleted_DoesNotAppendDuplicateCompletedHistory()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var documentId = Guid.CreateVersion7();
        var operation = PostingOperation.Unpost;
        var startedAtUtc = new DateTime(2026, 3, 7, 12, 0, 0, DateTimeKind.Utc);
        var completedAtUtc = startedAtUtc.AddSeconds(10);
        var secondCompletedAtUtc = startedAtUtc.AddMinutes(5);

        await InsertDraftDocumentAsync(Fixture.ConnectionString, documentId, "test.doc.history.duplicate_completion");
        await BeginAndCompleteAsync(host, documentId, operation, startedAtUtc, completedAtUtc);
        await MarkCompletedAsync(host, documentId, operation, secondCompletedAtUtc);

        var state = await ReadStateAsync(Fixture.ConnectionString, documentId, operation);
        state.Should().NotBeNull();
        state!.CompletedAtUtc.Should().Be(completedAtUtc);

        var events = await ReadHistoryEventRowsAsync(Fixture.ConnectionString, documentId, operation);
        events.Count(x => x.EventKind == StartedEvent).Should().Be(1);
        events.Count(x => x.EventKind == CompletedEvent).Should().Be(1);
        events.Single(x => x.EventKind == CompletedEvent).OccurredAtUtc.Should().Be(completedAtUtc);
    }

    private static async Task InsertDraftDocumentAsync(string connectionString, Guid documentId, string typeCode)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(CancellationToken.None);

        const string sql = """
                           INSERT INTO documents(
                               id, type_code, number, date_utc, status, posted_at_utc, marked_for_deletion_at_utc, created_at_utc, updated_at_utc
                           )
                           VALUES (
                               @id, @type_code, NULL, @date_utc, 1, NULL, NULL, NOW(), NOW()
                           );
                           """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", documentId);
        cmd.Parameters.AddWithValue("type_code", typeCode);
        cmd.Parameters.AddWithValue("date_utc", new DateTime(2026, 3, 7, 0, 0, 0, DateTimeKind.Utc));
        await cmd.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task InsertStaleInProgressStateAsync(
        string connectionString,
        Guid documentId,
        PostingOperation operation,
        Guid attemptId,
        DateTime startedAtUtc)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(CancellationToken.None);

        const string sql = """
                           INSERT INTO platform_document_operation_state(
                               document_id, operation, attempt_id, started_at_utc, completed_at_utc
                           )
                           VALUES (
                               @document_id, @operation, @attempt_id, @started_at_utc, NULL
                           );
                           """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("document_id", documentId);
        cmd.Parameters.AddWithValue("operation", (short)operation);
        cmd.Parameters.AddWithValue("attempt_id", attemptId);
        cmd.Parameters.AddWithValue("started_at_utc", startedAtUtc);
        await cmd.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task BeginAndCompleteAsync(
        IHost host,
        Guid documentId,
        PostingOperation operation,
        DateTime startedAtUtc,
        DateTime completedAtUtc)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var sp = scope.ServiceProvider;
        var uow = sp.GetRequiredService<IUnitOfWork>();
        var repo = sp.GetRequiredService<IDocumentOperationStateRepository>();

        await uow.BeginTransactionAsync(CancellationToken.None);
        try
        {
            var begin = await repo.TryBeginAsync(documentId, operation, startedAtUtc, CancellationToken.None);
            begin.Should().Be(PostingStateBeginResult.Begun);

            await repo.MarkCompletedAsync(documentId, operation, completedAtUtc, CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
        }
        catch
        {
            await uow.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task MarkCompletedAsync(
        IHost host,
        Guid documentId,
        PostingOperation operation,
        DateTime completedAtUtc)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var sp = scope.ServiceProvider;
        var uow = sp.GetRequiredService<IUnitOfWork>();
        var repo = sp.GetRequiredService<IDocumentOperationStateRepository>();

        await uow.BeginTransactionAsync(CancellationToken.None);
        try
        {
            await repo.MarkCompletedAsync(documentId, operation, completedAtUtc, CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
        }
        catch
        {
            await uow.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task<OperationStateRow?> ReadStateAsync(
        string connectionString,
        Guid documentId,
        PostingOperation operation)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(CancellationToken.None);

        const string sql = """
                           SELECT started_at_utc,
                                  completed_at_utc
                           FROM platform_document_operation_state
                           WHERE document_id = @document_id
                             AND operation = @operation;
                           """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("document_id", documentId);
        cmd.Parameters.AddWithValue("operation", (short)operation);

        await using var reader = await cmd.ExecuteReaderAsync(CancellationToken.None);
        if (!await reader.ReadAsync(CancellationToken.None))
            return null;

        return new OperationStateRow(
            reader.GetDateTime(0),
            reader.IsDBNull(1) ? null : reader.GetDateTime(1));
    }

    private static async Task<IReadOnlyList<HistoryEventRow>> ReadHistoryEventRowsAsync(
        string connectionString,
        Guid documentId,
        PostingOperation operation)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(CancellationToken.None);

        const string sql = """
                           SELECT event_kind,
                                  occurred_at_utc
                           FROM platform_document_operation_history
                           WHERE document_id = @document_id
                             AND operation = @operation
                           ORDER BY occurred_at_utc, history_id;
                           """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("document_id", documentId);
        cmd.Parameters.AddWithValue("operation", (short)operation);

        var list = new List<HistoryEventRow>();
        await using var reader = await cmd.ExecuteReaderAsync(CancellationToken.None);
        while (await reader.ReadAsync(CancellationToken.None))
        {
            list.Add(new HistoryEventRow(
                reader.GetInt16(0),
                reader.GetDateTime(1)));
        }

        return list;
    }

    private static async Task<IReadOnlyList<short>> ReadHistoryEventsAsync(
        string connectionString,
        Guid documentId,
        PostingOperation operation)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(CancellationToken.None);

        const string sql = """
                           SELECT event_kind
                           FROM platform_document_operation_history
                           WHERE document_id = @document_id
                             AND operation = @operation
                           ORDER BY occurred_at_utc, history_id;
                           """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("document_id", documentId);
        cmd.Parameters.AddWithValue("operation", (short)operation);

        var list = new List<short>();
        await using var reader = await cmd.ExecuteReaderAsync(CancellationToken.None);
        while (await reader.ReadAsync(CancellationToken.None))
            list.Add(reader.GetInt16(0));

        return list;
    }

    private sealed record OperationStateRow(DateTime StartedAtUtc, DateTime? CompletedAtUtc);

    private sealed record HistoryEventRow(short EventKind, DateTime OccurredAtUtc);
}
