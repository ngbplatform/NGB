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

namespace NGB.Runtime.IntegrationTests.Concurrency;

[Collection(PostgresCollection.Name)]
public sealed class UnpostRepostConcurrencyTests(PostgresTestFixture fixture)
{
    private const string Cash = "50";
    private const string Revenue = "90.1";

    [Fact]
    public async Task PostAndUnpostConcurrently_PostedBaseline_FinalStateIsUnpostedAndNoDuplicates()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var period = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var documentId = Guid.CreateVersion7();

        await SeedMinimalCoaAsync(host);
        await PostCashRevenueAsync(host, documentId, period, amount: 100m);

        using var barrier = new Barrier(participantCount: 2);

        // Act
        var postAgain = Task.Run(() => PostWithBarrierAsync(host, documentId, period, barrier));
        var unpost = Task.Run(() => UnpostWithBarrierAsync(host, documentId, barrier));

        await Task.WhenAll(postAgain, unpost);

        // Assert
        await AssertUnpostedStateAsync(host, documentId, period);
        await AssertPostingLogHasSingleCompletedRecordAsync(host, documentId, PostingOperation.Post);
        await AssertPostingLogHasSingleCompletedRecordAsync(host, documentId, PostingOperation.Unpost);
    }

    [Fact]
    public async Task UnpostAndRepostConcurrently_PostedBaseline_FinalStateIsConsistent_NoDoubleStorno()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var period = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var documentId = Guid.CreateVersion7();

        await SeedMinimalCoaAsync(host);
        await PostCashRevenueAsync(host, documentId, period, amount: 100m);

        using var barrier = new Barrier(participantCount: 2);

        // Act
        var unpost = Task.Run(() => UnpostWithBarrierAsync(host, documentId, barrier));
        var repost = Task.Run(() => RepostWithBarrierAsync(host, documentId, period, newAmount: 200m, barrier));

        await Task.WhenAll(unpost, repost);

        // Assert
        // We accept either outcome (depending on which operation wins), but we do NOT accept
        // double-storno or a mixed net effect.
        await AssertConsistentAfterUnpostVsRepostRaceAsync(host, documentId, period);

        await AssertPostingLogHasSingleCompletedRecordAsync(host, documentId, PostingOperation.Unpost);
        await AssertPostingLogHasSingleCompletedRecordAsync(host, documentId, PostingOperation.Repost);
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

    private static async Task PostCashRevenueAsync(IHost host, Guid documentId, DateTime period, decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var posting = sp.GetRequiredService<PostingEngine>();

        await posting.PostAsync(
            operation: PostingOperation.Post,
            postingAction: async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(
                    documentId: documentId,
                    period: period,
                    debit: chart.Get(Cash),
                    credit: chart.Get(Revenue),
                    amount: amount);
            },
            ct: CancellationToken.None);
    }

    private static async Task PostWithBarrierAsync(IHost host, Guid documentId, DateTime period, Barrier barrier)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var posting = sp.GetRequiredService<PostingEngine>();

        barrier.SignalAndWaitOrThrow(TimeSpan.FromSeconds(10));

        await posting.PostAsync(
            operation: PostingOperation.Post,
            postingAction: async (ctx, ct) =>
            {
                // Even if this delegate is executed, the posting must remain idempotent.
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(
                    documentId: documentId,
                    period: period,
                    debit: chart.Get(Cash),
                    credit: chart.Get(Revenue),
                    amount: 100m);
            },
            ct: CancellationToken.None);
    }

    private static async Task UnpostWithBarrierAsync(IHost host, Guid documentId, Barrier barrier)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var unposting = sp.GetRequiredService<UnpostingService>();

        barrier.SignalAndWaitOrThrow(TimeSpan.FromSeconds(10));

        await unposting.UnpostAsync(documentId, CancellationToken.None);
    }

    private static async Task RepostWithBarrierAsync(IHost host, Guid documentId, DateTime period, decimal newAmount, Barrier barrier)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var reposting = sp.GetRequiredService<RepostingService>();

        barrier.SignalAndWaitOrThrow(TimeSpan.FromSeconds(10));

        await reposting.RepostAsync(
            documentId: documentId,
            postNew: async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(
                    documentId: documentId,
                    period: period,
                    debit: chart.Get(Cash),
                    credit: chart.Get(Revenue),
                    amount: newAmount);
            },
            ct: CancellationToken.None);
    }

    private static async Task AssertUnpostedStateAsync(IHost host, Guid documentId, DateTime period)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        var entries = await entryReader.GetByDocumentAsync(documentId, CancellationToken.None);

        entries.Should().HaveCount(2);
        entries.Count(x => x.IsStorno).Should().Be(1);

        var turnoverReader = sp.GetRequiredService<IAccountingTurnoverReader>();
        var turnovers = await turnoverReader.GetForPeriodAsync(DateOnly.FromDateTime(period), CancellationToken.None);

        var cash = turnovers.Single(x => x.AccountCode == Cash);
        cash.DebitAmount.Should().Be(100m);
        cash.CreditAmount.Should().Be(100m);

        var revenue = turnovers.Single(x => x.AccountCode == Revenue);
        revenue.DebitAmount.Should().Be(100m);
        revenue.CreditAmount.Should().Be(100m);
    }

    private static async Task AssertConsistentAfterUnpostVsRepostRaceAsync(IHost host, Guid documentId, DateTime period)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        var entries = await entryReader.GetByDocumentAsync(documentId, CancellationToken.None);

        var stornoCount = entries.Count(x => x.IsStorno);

        var turnoverReader = sp.GetRequiredService<IAccountingTurnoverReader>();
        var turnovers = await turnoverReader.GetForPeriodAsync(DateOnly.FromDateTime(period), CancellationToken.None);

        decimal cashDebit = turnovers.Single(x => x.AccountCode == Cash).DebitAmount;
        decimal cashCredit = turnovers.Single(x => x.AccountCode == Cash).CreditAmount;
        decimal revenueDebit = turnovers.Single(x => x.AccountCode == Revenue).DebitAmount;
        decimal revenueCredit = turnovers.Single(x => x.AccountCode == Revenue).CreditAmount;

        bool looksLikeUnpost = entries.Count == 2
                               && stornoCount == 1
                               && cashDebit == 100m && cashCredit == 100m
                               && revenueDebit == 100m && revenueCredit == 100m;

        bool looksLikeRepost = entries.Count == 3
                               && stornoCount == 1
                               && cashDebit == 300m && cashCredit == 100m
                               && revenueDebit == 100m && revenueCredit == 300m;

        // Serializable outcome: Unpost then Repost (both completed under the document lock).
        // Baseline Post(100) -> Unpost(storno 100) -> Repost(storno {post,unpost} + new 200)
        bool looksLikeUnpostThenRepost = entries.Count == 5
                                        && stornoCount == 3
                                        && cashDebit == 400m && cashCredit == 200m
                                        && revenueDebit == 200m && revenueCredit == 400m;

        // Serializable outcome: Repost then Unpost (both completed under the document lock).
        // Baseline Post(100) -> Repost(storno 100 + new 200) -> Unpost(storno all existing entries)
        bool looksLikeRepostThenUnpost = entries.Count == 6
                                        && stornoCount == 4
                                        && cashDebit == 400m && cashCredit == 400m
                                        && revenueDebit == 400m && revenueCredit == 400m;

        (looksLikeUnpost || looksLikeRepost || looksLikeUnpostThenRepost || looksLikeRepostThenUnpost)
            .Should().BeTrue($"Unexpected state after Unpost vs Repost race. " +
                             $"Entries={entries.Count}, Storno={stornoCount}, " +
                             $"Cash(D={cashDebit},C={cashCredit}), " +
                             $"Revenue(D={revenueDebit},C={revenueCredit})");
    }

    private static async Task AssertPostingLogHasSingleCompletedRecordAsync(IHost host, Guid documentId, PostingOperation operation)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var logReader = sp.GetRequiredService<IPostingStateReader>();
        var page = await logReader.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = DateTime.UtcNow.AddDays(-7),
            ToUtc = DateTime.UtcNow.AddDays(7),
            DocumentId = documentId,
            Operation = operation
        }, CancellationToken.None);

        page.Records.Should().HaveCount(1);
        page.Records[0].Status.Should().Be(PostingStateStatus.Completed);
        page.Records[0].CompletedAtUtc.Should().NotBeNull();
    }
}
