using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.Periods;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.Periods;
using NGB.Persistence.Readers.PostingState;
using NGB.Runtime.Accounts;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Periods;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Periods;

[Collection(PostgresCollection.Name)]
public sealed class TwoPeriodsTriangleLockGranularityTests(PostgresTestFixture fixture)
{
    [Fact]
    public async Task CloseMonth_Jan_ConcurrentWith_RepostAndUnpost_Feb_NoDeadlock_JanClosed_FebOperationsSucceed()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var jan = new DateOnly(2026, 1, 1);
        var feb = new DateOnly(2026, 2, 1);

        var janUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var febUtc = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

        await SeedMinimalCoaAsync(host);

        Guid docA;
        Guid docB;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();

            docA = await drafts.CreateDraftAsync(typeCode: "test", number: null, dateUtc: febUtc, manageTransaction: true, ct: CancellationToken.None);
            docB = await drafts.CreateDraftAsync(typeCode: "test", number: null, dateUtc: febUtc, manageTransaction: true, ct: CancellationToken.None);

            await docs.PostAsync(
                docA,
                postingAction: async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    ctx.Post(docA, febUtc, chart.Get("50"), chart.Get("90.1"), 100m);
                },
                ct: CancellationToken.None);

            await docs.PostAsync(
                docB,
                postingAction: async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    ctx.Post(docB, febUtc, chart.Get("50"), chart.Get("90.1"), 50m);
                },
                ct: CancellationToken.None);
        }

        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Act (triangle across two different periods)
        var closeJanTask = RunCloseMonthOutcomeAsync(host, jan, gate);
        var repostFebTask = RunRepostOutcomeAsync(host, docA, febUtc, gate, newAmount: 200m);
        var unpostFebTask = RunUnpostOutcomeAsync(host, docB, gate);

        gate.SetResult(true);

        var outcomes = await Task.WhenAll(closeJanTask, repostFebTask, unpostFebTask)
            .WaitAsync(TimeSpan.FromSeconds(45));

        var closeOutcome = outcomes[0];
        var repostOutcome = outcomes[1];
        var unpostOutcome = outcomes[2];

        // Assert: CloseMonth(Jan) must succeed (or be already closed), and Feb operations must succeed.
        (closeOutcome.Error is null || closeOutcome.Error is PeriodAlreadyClosedException)
            .Should().BeTrue($"unexpected CloseMonth(Jan) error: {closeOutcome.Error}");

        repostOutcome.Error.Should().BeNull($"Repost(Feb) must not be affected by closing Jan. Error: {repostOutcome.Error}");
        unpostOutcome.Error.Should().BeNull($"Unpost(Feb) must not be affected by closing Jan. Error: {unpostOutcome.Error}");

        await using var verifyScope = host.Services.CreateAsyncScope();
        var sp = verifyScope.ServiceProvider;

        var closedReader = sp.GetRequiredService<IClosedPeriodReader>();
        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        var docRepo = sp.GetRequiredService<IDocumentRepository>();
        var postingLog = sp.GetRequiredService<IPostingStateReader>();

        // January is closed.
        var closedJan = await closedReader.GetClosedAsync(jan, jan, CancellationToken.None);
        closedJan.Should().ContainSingle(p => p.Period == jan);

        // DocA: repost applied (orig + storno + new).
        var docAEntries = await entryReader.GetByDocumentAsync(docA, CancellationToken.None);
        docAEntries.Should().HaveCount(3);
        docAEntries.Count(e => e.IsStorno).Should().Be(1);

        var docAState = await docRepo.GetAsync(docA, CancellationToken.None);
        docAState!.Status.Should().Be(DocumentStatus.Posted);

        // DocB: unpost applied (orig + storno).
        var docBEntries = await entryReader.GetByDocumentAsync(docB, CancellationToken.None);
        docBEntries.Should().HaveCount(2);
        docBEntries.Count(e => e.IsStorno).Should().Be(1);

        var docBState = await docRepo.GetAsync(docB, CancellationToken.None);
        docBState!.Status.Should().Be(DocumentStatus.Draft);

        // Posting log: exactly one per operation per document.
        var fromUtc = DateTime.UtcNow.AddHours(-2);
        var toUtc = DateTime.UtcNow.AddHours(2);

        var repostLog = await postingLog.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = fromUtc,
            ToUtc = toUtc,
            DocumentId = docA,
            Operation = PostingOperation.Repost,
            PageSize = 20
        }, CancellationToken.None);

        repostLog.Records.Should().HaveCount(1);
        repostLog.Records.Single().CompletedAtUtc.Should().NotBeNull();

        var unpostLog = await postingLog.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = fromUtc,
            ToUtc = toUtc,
            DocumentId = docB,
            Operation = PostingOperation.Unpost,
            PageSize = 20
        }, CancellationToken.None);

        unpostLog.Records.Should().HaveCount(1);
        unpostLog.Records.Single().CompletedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task CloseMonth_Feb_ConcurrentWith_RepostAndUnpost_Jan_NoDeadlock_FebRejectedByPrerequisite_JanOperationsSucceed()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var jan = new DateOnly(2026, 1, 1);
        var feb = new DateOnly(2026, 2, 1);

        var janUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var febUtc = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

        await SeedMinimalCoaAsync(host);

        Guid docA;
        Guid docB;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();

            docA = await drafts.CreateDraftAsync(typeCode: "test", number: null, dateUtc: janUtc, manageTransaction: true, ct: CancellationToken.None);
            docB = await drafts.CreateDraftAsync(typeCode: "test", number: null, dateUtc: janUtc, manageTransaction: true, ct: CancellationToken.None);

            await docs.PostAsync(
                docA,
                postingAction: async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    ctx.Post(docA, janUtc, chart.Get("50"), chart.Get("90.1"), 100m);
                },
                ct: CancellationToken.None);

            await docs.PostAsync(
                docB,
                postingAction: async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    ctx.Post(docB, janUtc, chart.Get("50"), chart.Get("90.1"), 50m);
                },
                ct: CancellationToken.None);
        }

        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Act (triangle across two different periods)
        var closeFebTask = RunCloseMonthOutcomeAsync(host, feb, gate);
        var repostJanTask = RunRepostOutcomeAsync(host, docA, janUtc, gate, newAmount: 200m);
        var unpostJanTask = RunUnpostOutcomeAsync(host, docB, gate);

        gate.SetResult(true);

        var outcomes = await Task.WhenAll(closeFebTask, repostJanTask, unpostJanTask)
            .WaitAsync(TimeSpan.FromSeconds(45));

        var closeOutcome = outcomes[0];
        var repostOutcome = outcomes[1];
        var unpostOutcome = outcomes[2];

        // Assert: CloseMonth(Feb) cannot skip Jan, but Jan operations must still succeed without deadlock.
        closeOutcome.Error.Should().BeOfType<MonthClosingPrerequisiteNotMetException>()
            .Which.NextClosablePeriod.Should().Be(jan);

        repostOutcome.Error.Should().BeNull($"Repost(Jan) must not be affected by closing Feb. Error: {repostOutcome.Error}");
        unpostOutcome.Error.Should().BeNull($"Unpost(Jan) must not be affected by closing Feb. Error: {unpostOutcome.Error}");

        await using var verifyScope = host.Services.CreateAsyncScope();
        var sp = verifyScope.ServiceProvider;

        var closedReader = sp.GetRequiredService<IClosedPeriodReader>();
        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        var docRepo = sp.GetRequiredService<IDocumentRepository>();
        var postingLog = sp.GetRequiredService<IPostingStateReader>();

        // February is not closed because January is still the next closable period.
        var closedFeb = await closedReader.GetClosedAsync(feb, feb, CancellationToken.None);
        closedFeb.Should().BeEmpty();

        // DocA: repost applied (orig + storno + new).
        var docAEntries = await entryReader.GetByDocumentAsync(docA, CancellationToken.None);
        docAEntries.Should().HaveCount(3);
        docAEntries.Count(e => e.IsStorno).Should().Be(1);

        var docAState = await docRepo.GetAsync(docA, CancellationToken.None);
        docAState!.Status.Should().Be(DocumentStatus.Posted);

        // DocB: unpost applied (orig + storno).
        var docBEntries = await entryReader.GetByDocumentAsync(docB, CancellationToken.None);
        docBEntries.Should().HaveCount(2);
        docBEntries.Count(e => e.IsStorno).Should().Be(1);

        var docBState = await docRepo.GetAsync(docB, CancellationToken.None);
        docBState!.Status.Should().Be(DocumentStatus.Draft);

        // Posting log: exactly one per operation per document.
        var fromUtc = DateTime.UtcNow.AddHours(-2);
        var toUtc = DateTime.UtcNow.AddHours(2);

        var repostLog = await postingLog.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = fromUtc,
            ToUtc = toUtc,
            DocumentId = docA,
            Operation = PostingOperation.Repost,
            PageSize = 20
        }, CancellationToken.None);

        repostLog.Records.Should().HaveCount(1);
        repostLog.Records.Single().CompletedAtUtc.Should().NotBeNull();

        var unpostLog = await postingLog.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = fromUtc,
            ToUtc = toUtc,
            DocumentId = docB,
            Operation = PostingOperation.Unpost,
            PageSize = 20
        }, CancellationToken.None);

        unpostLog.Records.Should().HaveCount(1);
        unpostLog.Records.Single().CompletedAtUtc.Should().NotBeNull();
    }

    private static async Task SeedMinimalCoaAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "50",
            Name: "Cash",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "90.1",
            Name: "Income",
            Type: AccountType.Income,
            StatementSection: StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);
    }

    private static async Task<Outcome> RunCloseMonthOutcomeAsync(IHost host, DateOnly period, TaskCompletionSource<bool> gate)
    {
        await gate.Task;

        try
        {
            await using var scope = host.Services.CreateAsyncScope();
            var closing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();
            await closing.CloseMonthAsync(period, closedBy: "test", ct: CancellationToken.None);
            return Outcome.Success();
        }
        catch (Exception ex)
        {
            return Outcome.Fail(ex);
        }
    }

    private static async Task<Outcome> RunUnpostOutcomeAsync(IHost host, Guid documentId, TaskCompletionSource<bool> gate)
    {
        await gate.Task;

        try
        {
            await using var scope = host.Services.CreateAsyncScope();
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
            await docs.UnpostAsync(documentId, CancellationToken.None);
            return Outcome.Success();
        }
        catch (Exception ex)
        {
            return Outcome.Fail(ex);
        }
    }

    private static async Task<Outcome> RunRepostOutcomeAsync(IHost host, Guid documentId, DateTime periodUtc, TaskCompletionSource<bool> gate, decimal newAmount)
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
                    ctx.Post(documentId, periodUtc, chart.Get("50"), chart.Get("90.1"), newAmount);
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
        public static Outcome Fail(Exception ex) => new(Error: ex);
    }
}
