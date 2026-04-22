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

[Collection(PostgresCollection.Name)]
public sealed class PostingLogStaleInProgressRecoveryTests(PostgresTestFixture fixture)
{
    [Fact]
    public async Task PostAsync_StaleInProgressPostingLog_AllowsSafeTakeover_AndCompletes()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var period = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var documentId = Guid.CreateVersion7();

        await SeedMinimalCoaAsync(host);

        // Simulate a crashed process that started posting long ago (completed_at_utc is NULL).
        var staleStartedAtUtc = DateTime.UtcNow.AddHours(-2);
        await InsertInProgressPostingLogRowAsync(
            fixture.ConnectionString,
            documentId,
            PostingOperation.Post,
            startedAtUtc: staleStartedAtUtc);

        // Act: new attempt should take over and complete.
        await PostOnceAsync(host, documentId, period, amount: 100m);

        // Assert: entries were written.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;

            var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
            var entries = await entryReader.GetByDocumentAsync(documentId, CancellationToken.None);
            entries.Should().HaveCount(1);

            // Assert: posting_log was taken over and marked completed.
            var logReader = sp.GetRequiredService<IPostingStateReader>();
            var page = await logReader.GetPageAsync(new PostingStatePageRequest
            {
                FromUtc = DateTime.UtcNow.AddHours(-6),
                ToUtc = DateTime.UtcNow.AddHours(6),
                DocumentId = documentId,
                Operation = PostingOperation.Post,
                PageSize = 10
            }, CancellationToken.None);

            page.Records.Should().HaveCount(1);
            var record = page.Records[0];

            record.Status.Should().Be(PostingStateStatus.Completed);
            record.CompletedAtUtc.Should().NotBeNull();
            record.StartedAtUtc.Should().BeAfter(staleStartedAtUtc, "stale in-progress record must be taken over by updating started_at_utc");
        }
    }

    [Fact]
    public async Task PostAsync_RecentInProgressPostingLog_Throws_InProgress_AndDoesNotWriteEntries()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var period = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var documentId = Guid.CreateVersion7();

        await SeedMinimalCoaAsync(host);

        // Simulate a concurrent attempt (started recently, still in progress).
        var startedAtUtc = DateTime.UtcNow.AddMinutes(-1);
        await InsertInProgressPostingLogRowAsync(
            fixture.ConnectionString,
            documentId,
            PostingOperation.Post,
            startedAtUtc);

        // Act
        Func<Task> act = () => PostOnceAsync(host, documentId, period, amount: 100m);

        // Assert
        await act.Should().ThrowAsync<PostingAlreadyInProgressException>()
            .WithMessage("*Posting is already in progress*documentId=*operation=Post*");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;

            var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
            var entries = await entryReader.GetByDocumentAsync(documentId, CancellationToken.None);
            entries.Should().BeEmpty("posting must not write entries if posting_log indicates InProgress");

            var logReader = sp.GetRequiredService<IPostingStateReader>();
            var page = await logReader.GetPageAsync(new PostingStatePageRequest
            {
                FromUtc = DateTime.UtcNow.AddHours(-6),
                ToUtc = DateTime.UtcNow.AddHours(6),
                DocumentId = documentId,
                Operation = PostingOperation.Post,
                PageSize = 10
            }, CancellationToken.None);

            page.Records.Should().HaveCount(1);
            var record = page.Records[0];

            record.Status.Should().Be(PostingStateStatus.InProgress);
            record.StartedAtUtc.Should().BeCloseTo(startedAtUtc, precision: TimeSpan.FromSeconds(5));
            record.CompletedAtUtc.Should().BeNull();
        }
    }

    private static async Task InsertInProgressPostingLogRowAsync(
        string connectionString,
        Guid documentId,
        PostingOperation operation,
        DateTime startedAtUtc)
    {
        // We intentionally bypass repositories: this is a fault-injection setup for integration tests.
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
