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
public sealed class RepostConcurrencyTests(PostgresTestFixture fixture)
{
    private const string Cash = "50";
    private const string Revenue = "90.1";

    [Fact]
    public async Task RepostAndRepostConcurrently_PostedBaseline_ExactlyOneRepostApplied_NoDoubleStorno()
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
        var repost200 = Task.Run(() => RepostWithBarrierAsync(host, documentId, period, newAmount: 200m, barrier));
        var repost300 = Task.Run(() => RepostWithBarrierAsync(host, documentId, period, newAmount: 300m, barrier));

        await Task.WhenAll(repost200, repost300);

        // Assert
        // Repost is idempotent by (document_id, operation), so only ONE repost must be applied
        // (either 200 or 300 depending on which task wins the race). We MUST NOT see double storno.
        await AssertSingleRepostOutcomeAsync(host, documentId, period, possibleNewAmounts: [200m, 300m]);

        await AssertPostingLogHasSingleCompletedRecordAsync(host, documentId, PostingOperation.Post);
        await AssertPostingLogHasSingleCompletedRecordAsync(host, documentId, PostingOperation.Repost);
    }

    [Fact]
    public async Task PostAndRepostConcurrently_PostedBaseline_FinalStateLooksLikeRepost_NoDoubleStorno()
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
        var repost = Task.Run(() => RepostWithBarrierAsync(host, documentId, period, newAmount: 200m, barrier));

        await Task.WhenAll(postAgain, repost);

        // Assert
        // Post is idempotent and must not interfere. Final state must look like a single repost to 200.
        await AssertSingleRepostOutcomeAsync(host, documentId, period, possibleNewAmounts: [200m]);

        await AssertPostingLogHasSingleCompletedRecordAsync(host, documentId, PostingOperation.Post);
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

    private static async Task AssertSingleRepostOutcomeAsync(
        IHost host,
        Guid documentId,
        DateTime period,
        IReadOnlyList<decimal> possibleNewAmounts)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        var entries = await entryReader.GetByDocumentAsync(documentId, CancellationToken.None);

        // Baseline has 1 entry. A single repost must add exactly 1 storno + 1 new entry.
        entries.Should().HaveCount(3);
        entries.Count(x => x.IsStorno).Should().Be(1, "a single repost must create exactly one storno entry");

        var turnoverReader = sp.GetRequiredService<IAccountingTurnoverReader>();
        var turnovers = await turnoverReader.GetForPeriodAsync(DateOnly.FromDateTime(period), CancellationToken.None);

        var cash = turnovers.Single(x => x.AccountCode == Cash);
        var revenue = turnovers.Single(x => x.AccountCode == Revenue);

        cash.CreditAmount.Should().Be(100m, "single repost must storno the original 100 once");
        revenue.DebitAmount.Should().Be(100m, "single repost must storno the original 100 once");

        // After repost: Cash debit = original 100 + newAmount; Revenue credit = original 100 + newAmount.
        var possibleCashDebits = possibleNewAmounts.Select(x => 100m + x).ToArray();

        possibleCashDebits.Should().Contain(cash.DebitAmount,
            $"expected cash debit to match one of repost outcomes: {string.Join(", ", possibleCashDebits)}");
        possibleCashDebits.Should().Contain(revenue.CreditAmount,
            $"expected revenue credit to match one of repost outcomes: {string.Join(", ", possibleCashDebits)}");

        // Sanity: double-entry symmetry (turnovers, same accounts).
        cash.DebitAmount.Should().Be(revenue.CreditAmount);
        cash.CreditAmount.Should().Be(revenue.DebitAmount);
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
