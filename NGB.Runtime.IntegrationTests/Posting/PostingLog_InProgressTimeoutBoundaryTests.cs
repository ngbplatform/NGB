using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.PostingState;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Posting;

/// <summary>
/// Verifies the exact boundary semantics of the "stale InProgress" timeout.
///
/// IMPORTANT:
/// The timeout constant currently lives in PostgresPostingStateRepository as:
///   TimeSpan.FromMinutes(10)
/// If you change it, update this test accordingly.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PostingLog_InProgressTimeoutBoundaryTests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly TimeSpan InProgressTimeout = TimeSpan.FromMinutes(10);

    [Fact]
    public async Task PostAsync_InProgressStartedBeforeTimeout_AllowsTakeover_AndCompletes()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var period = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var documentId = Guid.CreateVersion7();

        await SeedMinimalCoaAsync(host);

        // Arrange: started just BEFORE the timeout (stale) => takeover must be allowed.
        var now = DateTime.UtcNow;
        var staleStartedAtUtc = now - InProgressTimeout - TimeSpan.FromSeconds(2);
        await InsertInProgressPostingLogRowAsync(
            Fixture.ConnectionString,
            documentId,
            PostingOperation.Post,
            startedAtUtc: staleStartedAtUtc);

        // Act: new attempt should take over and complete.
        await PostOnceAsync(host, documentId, period, amount: 100m);

        // Assert: entries were written + posting_log marked completed with updated StartedAtUtc.
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        (await entryReader.GetByDocumentAsync(documentId, CancellationToken.None))
            .Should().HaveCount(1);

        var logReader = sp.GetRequiredService<IPostingStateReader>();
        var page = await logReader.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = now.AddHours(-6),
            ToUtc = now.AddHours(6),
            DocumentId = documentId,
            Operation = PostingOperation.Post,
            PageSize = 10
        }, CancellationToken.None);

        page.Records.Should().HaveCount(1);
        var record = page.Records[0];

        record.Status.Should().Be(PostingStateStatus.Completed);
        record.CompletedAtUtc.Should().NotBeNull();
        record.StartedAtUtc.Should().BeAfter(staleStartedAtUtc);
        record.StartedAtUtc.Should().BeCloseTo(now, precision: TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task PostAsync_InProgressStartedWithinTimeout_ThrowsInProgress_AndDoesNotWriteEntries()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var period = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var documentId = Guid.CreateVersion7();

        await SeedMinimalCoaAsync(host);

        // Arrange: started just WITHIN the timeout (still in progress) => must reject.
        var now = DateTime.UtcNow;
        var recentStartedAtUtc = now - InProgressTimeout + TimeSpan.FromSeconds(2);
        await InsertInProgressPostingLogRowAsync(
            Fixture.ConnectionString,
            documentId,
            PostingOperation.Post,
            startedAtUtc: recentStartedAtUtc);

        // Act
        Func<Task> act = () => PostOnceAsync(host, documentId, period, amount: 100m);

        // Assert
        await act.Should().ThrowAsync<PostingAlreadyInProgressException>()
            .WithMessage("*Posting is already in progress*documentId=*operation=Post*");

        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        (await entryReader.GetByDocumentAsync(documentId, CancellationToken.None))
            .Should().BeEmpty("posting must not write entries if posting_log indicates InProgress");

        var logReader = sp.GetRequiredService<IPostingStateReader>();
        var page = await logReader.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = now.AddHours(-6),
            ToUtc = now.AddHours(6),
            DocumentId = documentId,
            Operation = PostingOperation.Post,
            PageSize = 10
        }, CancellationToken.None);

        page.Records.Should().HaveCount(1);
        var record = page.Records[0];

        record.Status.Should().Be(PostingStateStatus.InProgress);
        record.CompletedAtUtc.Should().BeNull();
        record.StartedAtUtc.Should().BeCloseTo(recentStartedAtUtc, precision: TimeSpan.FromSeconds(10));
    }

    private static async Task InsertInProgressPostingLogRowAsync(
        string connectionString,
        Guid documentId,
        PostingOperation operation,
        DateTime startedAtUtc)
    {
        // Fault-injection setup: bypass repositories and insert directly.
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

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
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task SeedMinimalCoaAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var accounts = sp.GetRequiredService<IChartOfAccountsManagementService>();

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "50",
            Name: "Cash",
            AccountType.Asset,
            StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "90.1",
            Name: "Revenue",
            AccountType.Income,
            StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);
    }

    private static async Task PostOnceAsync(IHost host, Guid documentId, DateTime period, decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var posting = sp.GetRequiredService<PostingEngine>();

        await posting.PostAsync(
            operation: PostingOperation.Post,
            postingAction: async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                var debit = chart.Get("50");
                var credit = chart.Get("90.1");

                ctx.Post(documentId, period, debit, credit, amount);
            },
            ct: CancellationToken.None);
    }
}
