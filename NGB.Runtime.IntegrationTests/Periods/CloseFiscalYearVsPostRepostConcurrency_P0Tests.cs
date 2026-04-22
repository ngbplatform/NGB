using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.PostingState;
using NGB.Runtime.Accounts;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Periods;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Periods;

[Collection(PostgresCollection.Name)]
public sealed class CloseFiscalYearVsPostRepostConcurrency_P0Tests(PostgresTestFixture fixture)
{
    [Fact]
    public async Task CloseFiscalYear_And_Post_Concurrent_SameEndPeriod_NoDeadlock_CloseSeesEitherBeforeOrAfter_StateIsAtomic()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var endPeriod = new DateOnly(2026, 1, 1); // month start
        var periodUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var retainedEarningsId = await SeedCoaForFiscalYearCloseAsync(host);

        // Baseline P&L activity (must exist so CloseFiscalYear always posts closing entries).
        var baselineRevenue = 50m;
        _ = await CreateDraftAndPostRevenueAsync(host, periodUtc, new[] { baselineRevenue });

        // Concurrent post: multi-entry to detect partial writes.
        var concurrentRevenueParts = new[] { 60m, 40m };
        var concurrentRevenueTotal = concurrentRevenueParts.Sum();
        var docToPost = await CreateDraftAsync(host, periodUtc);

        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Act
        var closeTask = RunCloseFiscalYearOutcomeAsync(host, endPeriod, retainedEarningsId, gate);
        var postTask = RunPostOutcomeAsync(host, docToPost, periodUtc, concurrentRevenueParts, gate);

        gate.SetResult(true);

        var outcomes = await Task.WhenAll(closeTask, postTask)
            .WaitAsync(TimeSpan.FromSeconds(45));

        var closeOutcome = outcomes[0];
        var postOutcome = outcomes[1];

        // Assert: no deadlocks / timeouts; both operations should succeed.
        closeOutcome.Error.Should().BeNull($"CloseFiscalYear failed: {closeOutcome.Error}");
        postOutcome.Error.Should().BeNull($"Post failed: {postOutcome.Error}");

        var expectedCloseDocumentId = DeterministicGuid.Create($"CloseFiscalYear|{endPeriod:yyyy-MM-dd}");
        var expectedClosingRevenueAmounts = new[]
        {
            baselineRevenue, // CloseFiscalYear acquired the end-period lock before the concurrent Post
            baselineRevenue + concurrentRevenueTotal // CloseFiscalYear acquired the lock after the concurrent Post
        };

        await using var verifyScope = host.Services.CreateAsyncScope();
        var sp = verifyScope.ServiceProvider;

        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        var docRepo = sp.GetRequiredService<IDocumentRepository>();
        var postingLog = sp.GetRequiredService<IPostingStateReader>();

        // 1) Post must be atomic: either 0 or 2 would indicate failure/partial; expect exactly 2.
        var postedDoc = await docRepo.GetAsync(docToPost, CancellationToken.None);
        postedDoc.Should().NotBeNull();
        postedDoc!.Status.Should().Be(DocumentStatus.Posted);

        var docEntries = await entryReader.GetByDocumentAsync(docToPost, CancellationToken.None);
        docEntries.Should().HaveCount(2);
        docEntries.Count(e => e.IsStorno).Should().Be(0);

        // 2) CloseFiscalYear must be consistent: it sees either state-before or state-after concurrent Post.
        var closingEntries = await entryReader.GetByDocumentAsync(expectedCloseDocumentId, CancellationToken.None);
        closingEntries.Should().HaveCount(1);

        var revenueClose = closingEntries.Single();
        revenueClose.Debit.Code.Should().Be("90.1");
        revenueClose.Credit.Code.Should().Be("300");
        expectedClosingRevenueAmounts.Should().Contain(revenueClose.Amount);

        // 3) Posting log: both operations recorded as Completed.
        var fromUtc = DateTime.UtcNow.AddHours(-2);
        var toUtc = DateTime.UtcNow.AddHours(2);

        var closeLog = await postingLog.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = fromUtc,
            ToUtc = toUtc,
            DocumentId = expectedCloseDocumentId,
            Operation = PostingOperation.CloseFiscalYear,
            PageSize = 20
        }, CancellationToken.None);
        closeLog.Records.Should().ContainSingle(r =>
            r.DocumentId == expectedCloseDocumentId &&
            r.Operation == PostingOperation.CloseFiscalYear &&
            r.Status == PostingStateStatus.Completed);

        var postLog = await postingLog.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = fromUtc,
            ToUtc = toUtc,
            DocumentId = docToPost,
            Operation = PostingOperation.Post,
            PageSize = 20
        }, CancellationToken.None);
        postLog.Records.Should().ContainSingle(r =>
            r.DocumentId == docToPost &&
            r.Operation == PostingOperation.Post &&
            r.Status == PostingStateStatus.Completed);
    }

    [Fact]
    public async Task CloseFiscalYear_And_Repost_Concurrent_SameEndPeriod_NoDeadlock_CloseSeesEitherBeforeOrAfter_StateIsAtomic()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var endPeriod = new DateOnly(2026, 1, 1); // month start
        var periodUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var retainedEarningsId = await SeedCoaForFiscalYearCloseAsync(host);

        // Initial P&L activity: multi-entry document.
        var origParts = new[] { 20m, 30m };
        var origTotal = origParts.Sum();
        var doc = await CreateDraftAndPostRevenueAsync(host, periodUtc, origParts);

        // Repost new values: multi-entry.
        var newParts = new[] { 80m, 120m };
        var newTotal = newParts.Sum();

        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Act
        var closeTask = RunCloseFiscalYearOutcomeAsync(host, endPeriod, retainedEarningsId, gate);
        var repostTask = RunRepostOutcomeAsync(host, doc, periodUtc, newParts, gate);

        gate.SetResult(true);

        var outcomes = await Task.WhenAll(closeTask, repostTask)
            .WaitAsync(TimeSpan.FromSeconds(45));

        var closeOutcome = outcomes[0];
        var repostOutcome = outcomes[1];

        // Assert: no deadlocks / timeouts; both operations should succeed.
        closeOutcome.Error.Should().BeNull($"CloseFiscalYear failed: {closeOutcome.Error}");
        repostOutcome.Error.Should().BeNull($"Repost failed: {repostOutcome.Error}");

        var expectedCloseDocumentId = DeterministicGuid.Create($"CloseFiscalYear|{endPeriod:yyyy-MM-dd}");
        var expectedClosingRevenueAmounts = new[]
        {
            origTotal, // CloseFiscalYear acquired the end-period lock before the Repost
            newTotal   // CloseFiscalYear acquired the lock after the Repost
        };

        await using var verifyScope = host.Services.CreateAsyncScope();
        var sp = verifyScope.ServiceProvider;

        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        var docRepo = sp.GetRequiredService<IDocumentRepository>();
        var postingLog = sp.GetRequiredService<IPostingStateReader>();

        // 1) Repost must be atomic: original(2) + storno(2) + new(2).
        var docRow = await docRepo.GetAsync(doc, CancellationToken.None);
        docRow.Should().NotBeNull();
        docRow!.Status.Should().Be(DocumentStatus.Posted);

        var docEntries = await entryReader.GetByDocumentAsync(doc, CancellationToken.None);
        docEntries.Should().HaveCount(6);
        docEntries.Count(e => e.IsStorno).Should().Be(2);

        // 2) CloseFiscalYear must be consistent: it sees either state-before or state-after Repost.
        var closingEntries = await entryReader.GetByDocumentAsync(expectedCloseDocumentId, CancellationToken.None);
        closingEntries.Should().HaveCount(1);

        var revenueClose = closingEntries.Single();
        revenueClose.Debit.Code.Should().Be("90.1");
        revenueClose.Credit.Code.Should().Be("300");
        expectedClosingRevenueAmounts.Should().Contain(revenueClose.Amount);

        // 3) Posting log: both operations recorded as Completed.
        var fromUtc = DateTime.UtcNow.AddHours(-2);
        var toUtc = DateTime.UtcNow.AddHours(2);

        var closeLog = await postingLog.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = fromUtc,
            ToUtc = toUtc,
            DocumentId = expectedCloseDocumentId,
            Operation = PostingOperation.CloseFiscalYear,
            PageSize = 20
        }, CancellationToken.None);
        closeLog.Records.Should().ContainSingle(r =>
            r.DocumentId == expectedCloseDocumentId &&
            r.Operation == PostingOperation.CloseFiscalYear &&
            r.Status == PostingStateStatus.Completed);

        var repostLog = await postingLog.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = fromUtc,
            ToUtc = toUtc,
            DocumentId = doc,
            Operation = PostingOperation.Repost,
            PageSize = 20
        }, CancellationToken.None);
        repostLog.Records.Should().ContainSingle(r =>
            r.DocumentId == doc &&
            r.Operation == PostingOperation.Repost &&
            r.Status == PostingStateStatus.Completed);
    }

    private static async Task<Guid> SeedCoaForFiscalYearCloseAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var accounts = sp.GetRequiredService<IChartOfAccountsManagementService>();

        // Balance sheet
        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "50",
            Name: "Cash",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        // P&L
        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "90.1",
            Name: "Revenue",
            Type: AccountType.Income,
            StatementSection: StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        // Equity (retained earnings)
        var retainedEarningsId = await accounts.CreateAsync(new CreateAccountRequest(
            Code: "300",
            Name: "Retained Earnings",
            Type: AccountType.Equity,
            StatementSection: StatementSection.Equity,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        return retainedEarningsId;
    }

    private static async Task<Guid> CreateDraftAsync(IHost host, DateTime dateUtc)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();

        return await drafts.CreateDraftAsync(
            typeCode: "test",
            number: null,
            dateUtc: dateUtc,
            manageTransaction: true,
            ct: CancellationToken.None);
    }

    private static async Task<Guid> CreateDraftAndPostRevenueAsync(IHost host, DateTime periodUtc, IReadOnlyCollection<decimal> parts)
    {
        var documentId = await CreateDraftAsync(host, periodUtc);

        await using var scope = host.Services.CreateAsyncScope();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

        await docs.PostAsync(
            documentId,
            postingAction: async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                foreach (var amount in parts)
                    ctx.Post(documentId, periodUtc, chart.Get("50"), chart.Get("90.1"), amount);
            },
            ct: CancellationToken.None);

        return documentId;
    }

    private static async Task<Outcome> RunCloseFiscalYearOutcomeAsync(
        IHost host,
        DateOnly endPeriod,
        Guid retainedEarningsAccountId,
        TaskCompletionSource<bool> gate)
    {
        await gate.Task;

        try
        {
            await using var scope = host.Services.CreateAsyncScope();
            var closing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();
            await closing.CloseFiscalYearAsync(endPeriod, retainedEarningsAccountId, closedBy: "test", ct: CancellationToken.None);
            return Outcome.Success();
        }
        catch (Exception ex)
        {
            return Outcome.Fail(ex);
        }
    }

    private static async Task<Outcome> RunPostOutcomeAsync(
        IHost host,
        Guid documentId,
        DateTime periodUtc,
        IReadOnlyCollection<decimal> parts,
        TaskCompletionSource<bool> gate)
    {
        await gate.Task;

        try
        {
            await using var scope = host.Services.CreateAsyncScope();
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

            await docs.PostAsync(
                documentId,
                postingAction: async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    foreach (var amount in parts)
                        ctx.Post(documentId, periodUtc, chart.Get("50"), chart.Get("90.1"), amount);
                },
                ct: CancellationToken.None);

            return Outcome.Success();
        }
        catch (Exception ex)
        {
            return Outcome.Fail(ex);
        }
    }

    private static async Task<Outcome> RunRepostOutcomeAsync(
        IHost host,
        Guid documentId,
        DateTime periodUtc,
        IReadOnlyCollection<decimal> newParts,
        TaskCompletionSource<bool> gate)
    {
        await gate.Task;

        try
        {
            await using var scope = host.Services.CreateAsyncScope();
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

            await docs.RepostAsync(
                documentId,
                postNew: async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    foreach (var amount in newParts)
                        ctx.Post(documentId, periodUtc, chart.Get("50"), chart.Get("90.1"), amount);
                },
                ct: CancellationToken.None);

            return Outcome.Success();
        }
        catch (Exception ex)
        {
            return Outcome.Fail(ex);
        }
    }

    private sealed record Outcome(Exception? Error)
    {
        public static Outcome Success() => new(Error: null);
        public static Outcome Fail(Exception ex) => new(ex);
    }
}
