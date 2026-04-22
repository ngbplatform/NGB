using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NGB.Accounting.PostingState;
using NGB.Persistence.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(PostgresCollection.Name)]
public sealed class DocumentOperationState_History_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task TryBeginAndMarkCompleted_WritesStartedAndCompletedHistory()
    {
        await Fixture.ResetDatabaseAsync();
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
        events.Should().ContainInOrder((short)1, (short)2);
    }

    [Fact]
    public async Task TryBegin_WhenStateIsStale_AppendsSupersededAndNewStartedHistory()
    {
        await Fixture.ResetDatabaseAsync();
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
        events.Should().Contain((short)3);
        events.Should().Contain((short)1);
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
}
