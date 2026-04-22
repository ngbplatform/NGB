using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
public sealed class Posting_IdempotencyKey_Matrix_EndToEndTests(PostgresTestFixture fixture)
{
    private const string Cash = "50";
    private const string Revenue = "90.1";

    [Fact]
    public async Task PostAsync_SameDocumentAndOperation_WhenAlreadyCompleted_DoesNotDuplicateEntries_AndKeepsPostingLogCompleted()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        await SeedMinimalCoaAsync(host);

        var period = new DateTime(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc);
        var documentId = Guid.CreateVersion7();

        // First post completes successfully.
        await PostOnceAsync(host, documentId, period, amount: 100m);

        // Act: replay same (DocumentId, Operation).
        // NOTE: current PostingEngine design executes postingAction before idempotency short-circuit,
        // so the contract we enforce here is "no duplicates" and stable log.
        var invoked = false;

        Func<Task> act = async () =>
        {
            await using var scopePosting = host.Services.CreateAsyncScope();
            var posting = scopePosting.ServiceProvider.GetRequiredService<PostingEngine>();

            await posting.PostAsync(
                operation: PostingOperation.Post,
                postingAction: async (ctx, ct) =>
                {
                    invoked = true;

                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    ctx.Post(documentId, period, chart.Get(Cash), chart.Get(Revenue), 100m);
                },
                manageTransaction: true,
                ct: CancellationToken.None);
        };

        await act.Should().NotThrowAsync();

        invoked.Should().BeTrue("PostingEngine currently executes postingAction even for completed idempotent replays");

        // Assert: entries are not duplicated.
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        (await entryReader.GetByDocumentAsync(documentId, CancellationToken.None)).Should().HaveCount(1);

        var logReader = sp.GetRequiredService<IPostingStateReader>();
        var logPage = await logReader.GetPageAsync(new PostingStatePageRequest
        {
            DocumentId = documentId,
            Operation = PostingOperation.Post,
            PageSize = 10,
            StaleAfter = TimeSpan.FromDays(3650)
        }, CancellationToken.None);

        logPage.Records.Should().HaveCount(1);
        logPage.Records[0].Status.Should().Be(PostingStateStatus.Completed);
    }

    [Fact]
    public async Task PostAsync_SameDocument_DifferentOperations_AreIndependent()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        await SeedMinimalCoaAsync(host);

        var period = new DateTime(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc);
        var month = new DateOnly(2026, 1, 1);
        var documentId = Guid.CreateVersion7();

        await PostOnceAsync(host, documentId, period, amount: 100m);

        // Unpost is a different operation (different idempotency key) and should succeed.
        await using (var scopeUnpost = host.Services.CreateAsyncScope())
        {
            var unposting = scopeUnpost.ServiceProvider.GetRequiredService<UnpostingService>();
            await unposting.UnpostAsync(documentId, CancellationToken.None);
        }

        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        // Assert: document entries remain (platform keeps history) and storno entries are added.
        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        var entries = await entryReader.GetByDocumentAsync(documentId, CancellationToken.None);
        entries.Should().HaveCount(2);
        entries.Count(e => e.IsStorno).Should().Be(1);

        // Assert turnovers exist for both accounts and have equal debit/credit totals.
        var chartProvider = sp.GetRequiredService<IChartOfAccountsProvider>();
        var chart = await chartProvider.GetAsync(CancellationToken.None);
        var cashId = chart.Get(Cash).Id;
        var revenueId = chart.Get(Revenue).Id;

        var turnoverReader = sp.GetRequiredService<IAccountingTurnoverReader>();
        var turnovers = await turnoverReader.GetForPeriodAsync(month, CancellationToken.None);

        var cash = turnovers.Single(x => x.AccountId == cashId);
        cash.DebitAmount.Should().Be(100m);
        cash.CreditAmount.Should().Be(100m);

        var revenue = turnovers.Single(x => x.AccountId == revenueId);
        revenue.DebitAmount.Should().Be(100m);
        revenue.CreditAmount.Should().Be(100m);

        // Posting log should contain completed records for both operations.
        var logReader = sp.GetRequiredService<IPostingStateReader>();

        var postLog = await logReader.GetPageAsync(new PostingStatePageRequest
        {
            DocumentId = documentId,
            Operation = PostingOperation.Post,
            PageSize = 10,
            StaleAfter = TimeSpan.FromDays(3650)
        }, CancellationToken.None);

        postLog.Records.Should().HaveCount(1);
        postLog.Records[0].Status.Should().Be(PostingStateStatus.Completed);

        var unpostLog = await logReader.GetPageAsync(new PostingStatePageRequest
        {
            DocumentId = documentId,
            Operation = PostingOperation.Unpost,
            PageSize = 10,
            StaleAfter = TimeSpan.FromDays(3650)
        }, CancellationToken.None);

        unpostLog.Records.Should().HaveCount(1);
        unpostLog.Records[0].Status.Should().Be(PostingStateStatus.Completed);
    }

    private static async Task PostOnceAsync(IHost host, Guid documentId, DateTime period, decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();
        await posting.PostAsync(
            operation: PostingOperation.Post,
            postingAction: async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(documentId, period, chart.Get(Cash), chart.Get(Revenue), amount);
            },
            manageTransaction: true,
            ct: CancellationToken.None);
    }

    private static async Task SeedMinimalCoaAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var accounts = sp.GetRequiredService<IChartOfAccountsManagementService>();

        await accounts.CreateAsync(new CreateAccountRequest(
            Cash,
            "Cash",
            AccountType.Asset,
            StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Revenue,
            "Revenue",
            AccountType.Income,
            StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);
    }
}
