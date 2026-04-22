using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.PostingState;
using Npgsql;
using NGB.Persistence.PostingState;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Posting;

/// <summary>
/// Covers the exact (tick-level) inequality boundary used by <see cref="IPostingStateRepository"/>
/// when deciding whether an InProgress record is stale and can be taken over.
///
/// Implementation detail (current):
/// - Stale takeover is allowed only if: existing.started_at_utc &lt; (attempt.started_at_utc - timeout)
/// - When existing.started_at_utc == cutoff exactly, it must remain InProgress.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PostingLogRepository_InProgressTimeoutBoundary_ExactCutoff_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly TimeSpan InProgressTimeout = TimeSpan.FromMinutes(10);

    [Fact]
    public async Task TryBegin_WhenExistingStartedAtIsExactlyCutoff_ReturnsInProgress_AndDoesNotUpdateRow()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var documentId = Guid.CreateVersion7();
        var attemptStartedAtUtc = new DateTime(2026, 2, 3, 12, 0, 0, DateTimeKind.Utc);
        var cutoffUtc = attemptStartedAtUtc - InProgressTimeout;

        await InsertInProgressRowAsync(Fixture.ConnectionString, documentId, PostingOperation.Post, cutoffUtc);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            var uow = sp.GetRequiredService<IUnitOfWork>();
            var repo = sp.GetRequiredService<IPostingStateRepository>();

            await uow.BeginTransactionAsync(CancellationToken.None);
            try
            {
                var result = await repo.TryBeginAsync(documentId, PostingOperation.Post, attemptStartedAtUtc, CancellationToken.None);
                result.Should().Be(PostingStateBeginResult.InProgress, "existing started_at_utc == cutoff must NOT be considered stale");

                await uow.CommitAsync(CancellationToken.None);
            }
            catch
            {
                await uow.RollbackAsync(CancellationToken.None);
                throw;
            }
        }

        var row = await ReadRowAsync(Fixture.ConnectionString, documentId, PostingOperation.Post);
        row.CompletedAtUtc.Should().BeNull();
        row.StartedAtUtc.Should().Be(cutoffUtc, "TryBegin must not mutate started_at_utc when it returns InProgress");
    }

    [Fact]
    public async Task TryBegin_WhenExistingStartedAtIsOneTickBeforeCutoff_AllowsTakeover_AndUpdatesStartedAt()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var documentId = Guid.CreateVersion7();
        var attemptStartedAtUtc = new DateTime(2026, 2, 3, 12, 0, 0, DateTimeKind.Utc);
        var cutoffUtc = attemptStartedAtUtc - InProgressTimeout;
        var staleByOneTickUtc = cutoffUtc.AddTicks(-1);

        await InsertInProgressRowAsync(Fixture.ConnectionString, documentId, PostingOperation.Post, staleByOneTickUtc);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            var uow = sp.GetRequiredService<IUnitOfWork>();
            var repo = sp.GetRequiredService<IPostingStateRepository>();

            await uow.BeginTransactionAsync(CancellationToken.None);
            try
            {
                var result = await repo.TryBeginAsync(documentId, PostingOperation.Post, attemptStartedAtUtc, CancellationToken.None);
                result.Should().Be(PostingStateBeginResult.Begun, "existing started_at_utc < cutoff must be considered stale and taken over");

                await uow.CommitAsync(CancellationToken.None);
            }
            catch
            {
                await uow.RollbackAsync(CancellationToken.None);
                throw;
            }
        }

        var row = await ReadRowAsync(Fixture.ConnectionString, documentId, PostingOperation.Post);
        row.CompletedAtUtc.Should().BeNull();
        row.StartedAtUtc.Should().Be(attemptStartedAtUtc, "takeover must atomically replace started_at_utc with the new attempt time");
    }

    private static async Task InsertInProgressRowAsync(
        string connectionString,
        Guid documentId,
        PostingOperation operation,
        DateTime startedAtUtc)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(CancellationToken.None);

        const string sql = """
                           INSERT INTO accounting_posting_state(
                               document_id, operation, started_at_utc, completed_at_utc
                           )
                           VALUES (@document_id, @operation, @started_at_utc, NULL);
                           """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("document_id", documentId);
        cmd.Parameters.AddWithValue("operation", (short)operation);
        cmd.Parameters.AddWithValue("started_at_utc", startedAtUtc);
        await cmd.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task<(DateTime StartedAtUtc, DateTime? CompletedAtUtc)> ReadRowAsync(
        string connectionString,
        Guid documentId,
        PostingOperation operation)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(CancellationToken.None);

        const string sql = """
                           SELECT started_at_utc, completed_at_utc
                           FROM accounting_posting_state
                           WHERE document_id = @document_id AND operation = @operation;
                           """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("document_id", documentId);
        cmd.Parameters.AddWithValue("operation", (short)operation);

        await using var reader = await cmd.ExecuteReaderAsync(CancellationToken.None);
        if (!await reader.ReadAsync(CancellationToken.None))
            throw new XunitException("Expected posting_log row to exist.");

        return (reader.GetFieldValue<DateTime>(0), reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTime>(1));
    }
}
