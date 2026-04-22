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
public sealed class UnpostConcurrencyTests(PostgresTestFixture fixture)
{
    private const string Cash = "50";
    private const string Revenue = "90.1";

    [Fact]
    public async Task UnpostAndUnpostConcurrently_PostedBaseline_WritesExactlyOneStorno_AndNetEffectIsZero()
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
        var unpost1 = Task.Run(() => UnpostWithBarrierAsync(host, documentId, barrier));
        var unpost2 = Task.Run(() => UnpostWithBarrierAsync(host, documentId, barrier));

        await Task.WhenAll(unpost1, unpost2);

        // Assert
        await AssertSingleUnpostOutcomeAsync(host, documentId, period);

        await AssertPostingLogHasSingleCompletedRecordAsync(host, documentId, PostingOperation.Post);
        await AssertPostingLogHasSingleCompletedRecordAsync(host, documentId, PostingOperation.Unpost);
    }

    [Fact]
    public async Task PostPostUnpostConcurrently_PostedBaseline_FinalStateIsUnposted_AndNoDuplicates()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var period = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var documentId = Guid.CreateVersion7();

        await SeedMinimalCoaAsync(host);
        await PostCashRevenueAsync(host, documentId, period, amount: 100m);

        using var barrier = new Barrier(participantCount: 3);

        // Act
        var post1 = Task.Run(() => PostWithBarrierAsync(host, documentId, period, barrier));
        var post2 = Task.Run(() => PostWithBarrierAsync(host, documentId, period, barrier));
        var unpost = Task.Run(() => UnpostWithBarrierAsync(host, documentId, barrier));

        await Task.WhenAll(post1, post2, unpost);

        // Assert
        await AssertSingleUnpostOutcomeAsync(host, documentId, period);

        await AssertPostingLogHasSingleCompletedRecordAsync(host, documentId, PostingOperation.Post);
        await AssertPostingLogHasSingleCompletedRecordAsync(host, documentId, PostingOperation.Unpost);
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
                // Even if executed, the posting must remain idempotent.
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

    private static async Task AssertSingleUnpostOutcomeAsync(IHost host, Guid documentId, DateTime period)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        var entries = await entryReader.GetByDocumentAsync(documentId, CancellationToken.None);

        // Baseline has 1 entry. A single unpost must add exactly 1 storno.
        entries.Should().HaveCount(2);
        entries.Count(x => x.IsStorno).Should().Be(1);

        var turnoverReader = sp.GetRequiredService<IAccountingTurnoverReader>();
        var turnovers = await turnoverReader.GetForPeriodAsync(DateOnly.FromDateTime(period), CancellationToken.None);

        var cash = turnovers.Single(x => x.AccountCode == Cash);
        var revenue = turnovers.Single(x => x.AccountCode == Revenue);

        cash.DebitAmount.Should().Be(100m);
        cash.CreditAmount.Should().Be(100m);
        revenue.DebitAmount.Should().Be(100m);
        revenue.CreditAmount.Should().Be(100m);

        // Double-entry symmetry sanity.
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
