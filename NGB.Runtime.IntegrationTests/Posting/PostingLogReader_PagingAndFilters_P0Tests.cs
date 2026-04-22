using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Persistence.Readers.PostingState;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.IntegrationTests.Reporting;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Posting;

[Collection(PostgresCollection.Name)]
public sealed class PostingLogReader_PagingAndFilters_P0Tests(PostgresTestFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task PostingLogReader_KeysetPaging_ReturnsAllRecords_NoDuplicates_StableOrder_AndCursorSemantics()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        // Create many posting_log rows.
        // Note: Posting is idempotent per (DocumentId, Operation), so we must use unique document ids.
        var docs = Enumerable.Range(0, 8).Select(_ => Guid.CreateVersion7()).ToArray();

        foreach (var d in docs)
            await ReportingTestHelpers.PostAsync(host, d, ReportingTestHelpers.Day1Utc, debitCode: "50", creditCode: "90.1", amount: 10m);

        // Add a few Unpost records (different operation values in the log).
        foreach (var d in docs.Take(3))
            await ReportingTestHelpers.UnpostAsync(host, d);

        var expectedCount = docs.Length + 3;

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IPostingStateReader>();

        var all = new List<PostingStateRecord>();

        PostingStateCursor? cursor = null;
        while (true)
        {
            var page = await reader.GetPageAsync(new PostingStatePageRequest
            {
                Cursor = cursor,
                PageSize = 3,

                // Make status classification deterministic: no record should be considered stale.
                StaleAfter = TimeSpan.FromDays(3650)
            }, CancellationToken.None);

            // Cursor invariants.
            if (cursor is not null && page.Records.Count > 0)
            {
                IsStrictlyAfter(page.Records[0], cursor)
                    .Should().BeTrue("first record on the next page must be strictly after the cursor (keyset semantics)");
            }

            if (page.HasMore)
            {
                page.Records.Should().NotBeEmpty("HasMore=true must never return an empty page");
                page.NextCursor.Should().NotBeNull("HasMore=true must provide NextCursor");

                var last = page.Records[^1];
                page.NextCursor!.AfterStartedAtUtc.Should().Be(last.StartedAtUtc);
                page.NextCursor.AfterDocumentId.Should().Be(last.DocumentId);
                page.NextCursor.AfterOperation.Should().Be((short)last.Operation);
            }
            else
            {
                page.NextCursor.Should().BeNull("HasMore=false must not provide NextCursor");
            }

            all.AddRange(page.Records);

            if (!page.HasMore)
                break;

            cursor = page.NextCursor;
        }

        all.Should().HaveCount(expectedCount);

        // No duplicates by (DocumentId, Operation).
        all.Select(r => (r.DocumentId, r.Operation))
            .Distinct()
            .Should().HaveCount(expectedCount);

        // Sorted by started_at DESC, document_id DESC, operation DESC.
        for (var i = 1; i < all.Count; i++)
        {
            var prev = all[i - 1];
            var cur = all[i];

            IsNonIncreasing(prev, cur)
                .Should().BeTrue($"posting log must be ordered by (started_at_utc DESC, document_id DESC, operation DESC); violation at index {i}");
        }

        // All created by successful operations, so should be Completed.
        all.Should().OnlyContain(r => r.Status == PostingStateStatus.Completed);

        // Operation filter must return only Post records.
        var postOnly = await reader.GetPageAsync(new PostingStatePageRequest
        {
            Operation = PostingOperation.Post,
            PageSize = 10_000,
            StaleAfter = TimeSpan.FromDays(3650)
        }, CancellationToken.None);

        postOnly.Records.Should().HaveCount(docs.Length);
        postOnly.Records.Should().OnlyContain(r => r.Operation == PostingOperation.Post);

        // Document filter should return both Post and Unpost for a doc that was unposted.
        var oneDoc = docs[0];
        var docPage = await reader.GetPageAsync(new PostingStatePageRequest
        {
            DocumentId = oneDoc,
            PageSize = 10,
            StaleAfter = TimeSpan.FromDays(3650)
        }, CancellationToken.None);

        docPage.Records.Select(r => r.Operation)
            .Should().BeEquivalentTo(new[] { PostingOperation.Unpost, PostingOperation.Post }, o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task PostingLogReader_StatusFilters_Work_ForCompleted_InProgress_Stale()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        // Seed table directly: this test is for PostingLogReader classification/filtering.
        var completedDoc = Guid.CreateVersion7();
        var inProgressDoc = Guid.CreateVersion7();
        var staleDoc = Guid.CreateVersion7();

        var nowUtc = DateTime.UtcNow;

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();

            const string insertCompleted = """
                                           INSERT INTO accounting_posting_state(document_id, operation, started_at_utc, completed_at_utc)
                                           VALUES (@document_id, @operation, @started_at_utc, @completed_at_utc);
                                           """;

            const string insertInProgress = """
                                            INSERT INTO accounting_posting_state(document_id, operation, started_at_utc, completed_at_utc)
                                            VALUES (@document_id, @operation, @started_at_utc, NULL);
                                            """;

            // Completed.
            await using (var cmd = new NpgsqlCommand(insertCompleted, conn))
            {
                cmd.Parameters.AddWithValue("document_id", completedDoc);
                cmd.Parameters.AddWithValue("operation", (short)PostingOperation.Post);
                cmd.Parameters.AddWithValue("started_at_utc", nowUtc.AddMinutes(-5));
                cmd.Parameters.AddWithValue("completed_at_utc", nowUtc.AddMinutes(-4));
                await cmd.ExecuteNonQueryAsync();
            }

            // InProgress (not stale).
            await using (var cmd = new NpgsqlCommand(insertInProgress, conn))
            {
                cmd.Parameters.AddWithValue("document_id", inProgressDoc);
                cmd.Parameters.AddWithValue("operation", (short)PostingOperation.Post);
                cmd.Parameters.AddWithValue("started_at_utc", nowUtc.AddMinutes(-2));
                await cmd.ExecuteNonQueryAsync();
            }

            // Stale.
            await using (var cmd = new NpgsqlCommand(insertInProgress, conn))
            {
                cmd.Parameters.AddWithValue("document_id", staleDoc);
                cmd.Parameters.AddWithValue("operation", (short)PostingOperation.Post);
                cmd.Parameters.AddWithValue("started_at_utc", nowUtc.AddDays(-5));
                await cmd.ExecuteNonQueryAsync();
            }
        }

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IPostingStateReader>();

        var staleAfter = TimeSpan.FromDays(1);

        var completed = await reader.GetPageAsync(new PostingStatePageRequest
        {
            Status = PostingStateStatus.Completed,
            PageSize = 100,
            StaleAfter = staleAfter
        }, CancellationToken.None);

        completed.Records.Should().ContainSingle(r => r.DocumentId == completedDoc);
        completed.Records.Single(r => r.DocumentId == completedDoc).Status.Should().Be(PostingStateStatus.Completed);

        var inProgress = await reader.GetPageAsync(new PostingStatePageRequest
        {
            Status = PostingStateStatus.InProgress,
            PageSize = 100,
            StaleAfter = staleAfter
        }, CancellationToken.None);

        inProgress.Records.Should().ContainSingle(r => r.DocumentId == inProgressDoc);
        inProgress.Records.Single(r => r.DocumentId == inProgressDoc).Status.Should().Be(PostingStateStatus.InProgress);

        var stale = await reader.GetPageAsync(new PostingStatePageRequest
        {
            Status = PostingStateStatus.StaleInProgress,
            PageSize = 100,
            StaleAfter = staleAfter
        }, CancellationToken.None);

        stale.Records.Should().ContainSingle(r => r.DocumentId == staleDoc);
        stale.Records.Single(r => r.DocumentId == staleDoc).Status.Should().Be(PostingStateStatus.StaleInProgress);
    }

    private static bool IsNonIncreasing(PostingStateRecord prev, PostingStateRecord cur)
    {
        // DESC ordering: started_at DESC, document_id DESC, operation DESC
        if (cur.StartedAtUtc < prev.StartedAtUtc) return true;
        if (cur.StartedAtUtc > prev.StartedAtUtc) return false;

        if (cur.DocumentId.CompareTo(prev.DocumentId) < 0) return true;
        if (cur.DocumentId.CompareTo(prev.DocumentId) > 0) return false;

        return (short)cur.Operation <= (short)prev.Operation;
    }

    private static bool IsStrictlyAfter(PostingStateRecord firstOfNextPage, PostingStateCursor cursor)
    {
        // For DESC ordering, the next page must contain keys strictly "less" than the cursor key:
        // started_at < cursor.started_at OR started_at== AND doc_id < OR doc== AND op <
        if (firstOfNextPage.StartedAtUtc < cursor.AfterStartedAtUtc) return true;
        if (firstOfNextPage.StartedAtUtc > cursor.AfterStartedAtUtc) return false;

        var docCmp = firstOfNextPage.DocumentId.CompareTo(cursor.AfterDocumentId);
        if (docCmp < 0) return true;
        if (docCmp > 0) return false;

        return (short)firstOfNextPage.Operation < cursor.AfterOperation;
    }
}
