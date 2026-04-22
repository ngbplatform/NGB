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
using NGB.Runtime.Posting;

namespace NGB.Runtime.IntegrationTests.Periods;

[Collection(PostgresCollection.Name)]
public sealed class CloseMonthVsUnpostRepost_DifferentDocuments_Concurrency_P0Tests(PostgresTestFixture fixture)
{
    [Fact]
    public async Task RepostDocA_UnpostDocB_CloseMonth_Concurrent_SamePeriod_NoDeadlock_MonthClosed_OperationsAreAtomicAndIsolated()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var period = new DateOnly(2026, 1, 1);
        var periodUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        await SeedMinimalCoaAsync(host);

        var docA = await CreateAndPostAsync(host, periodUtc, amount: 100m);
        var docB = await CreateAndPostAsync(host, periodUtc, amount: 50m);

        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Act
        var closeTask = RunCloseMonthOutcomeAsync(host, period, gate);
        var repostTask = RunRepostOutcomeAsync(host, docA, periodUtc, gate, newAmount: 200m);
        var unpostTask = RunUnpostOutcomeAsync(host, docB, gate);

        gate.SetResult(true);

        var outcomes = await Task.WhenAll(closeTask, repostTask, unpostTask)
            .WaitAsync(TimeSpan.FromSeconds(45));

        var closeOutcome = outcomes[0];
        var repostOutcome = outcomes[1];
        var unpostOutcome = outcomes[2];

        // Assert: month must be closed at the end, and we must not deadlock.
        (closeOutcome.Error is null || closeOutcome.Error is PeriodAlreadyClosedException)
            .Should().BeTrue($"unexpected CloseMonth error: {closeOutcome.Error}");

        await using var verifyScope = host.Services.CreateAsyncScope();
        var sp = verifyScope.ServiceProvider;

        var closedReader = sp.GetRequiredService<IClosedPeriodReader>();
        var docRepo = sp.GetRequiredService<IDocumentRepository>();
        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        var postingLog = sp.GetRequiredService<IPostingStateReader>();

        var closed = await closedReader.GetClosedAsync(period, period, CancellationToken.None);
        closed.Should().ContainSingle(p => p.Period == period);

        // Read current state
        var aDoc = await docRepo.GetAsync(docA, CancellationToken.None);
        var bDoc = await docRepo.GetAsync(docB, CancellationToken.None);

        aDoc.Should().NotBeNull();
        bDoc.Should().NotBeNull();

        var aEntries = await entryReader.GetByDocumentAsync(docA, CancellationToken.None);
        var bEntries = await entryReader.GetByDocumentAsync(docB, CancellationToken.None);

        // Posting logs for operations
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

        var unpostLog = await postingLog.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = fromUtc,
            ToUtc = toUtc,
            DocumentId = docB,
            Operation = PostingOperation.Unpost,
            PageSize = 20
        }, CancellationToken.None);

        // Doc A: Repost either applied fully, or rejected due to closed period (no side effects).
        if (repostOutcome.Error is null)
        {
            aEntries.Should().HaveCount(3);
            aEntries.Count(e => e.IsStorno).Should().Be(1);
            aDoc!.Status.Should().Be(DocumentStatus.Posted);

            repostLog.Records.Should().HaveCount(1);
            repostLog.Records.Single().CompletedAtUtc.Should().NotBeNull();
        }
        else
        {
            repostOutcome.Error.Should().BeOfType<PostingPeriodClosedException>();
            repostOutcome.Error!.Message.Should().NotBeNullOrWhiteSpace();
            repostOutcome.Error.Message.ToLowerInvariant().Should().Contain(
                "closed",
                $"expected closed-period rejection, but got: {repostOutcome.Error.Message}");

            // Only the original posting remains.
            aEntries.Should().HaveCount(1);
            aEntries.Count(e => e.IsStorno).Should().Be(0);
            aDoc!.Status.Should().Be(DocumentStatus.Posted);

            repostLog.Records.Should().BeEmpty();
        }

        // Doc B: Unpost either applied fully, or rejected due to closed period (no side effects).
        if (unpostOutcome.Error is null)
        {
            bEntries.Should().HaveCount(2);
            bEntries.Count(e => e.IsStorno).Should().Be(1);
            bDoc!.Status.Should().Be(DocumentStatus.Draft);

            unpostLog.Records.Should().HaveCount(1);
            unpostLog.Records.Single().CompletedAtUtc.Should().NotBeNull();
        }
        else
        {
            unpostOutcome.Error.Should().BeOfType<PostingPeriodClosedException>();
            unpostOutcome.Error!.Message.Should().NotBeNullOrWhiteSpace();
            unpostOutcome.Error.Message.ToLowerInvariant().Should().Contain(
                "closed",
                $"expected closed-period rejection, but got: {unpostOutcome.Error.Message}");

            // Only the original posting remains.
            bEntries.Should().HaveCount(1);
            bEntries.Count(e => e.IsStorno).Should().Be(0);
            bDoc!.Status.Should().Be(DocumentStatus.Posted);

            unpostLog.Records.Should().BeEmpty();
        }
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

    private static async Task<Guid> CreateAndPostAsync(IHost host, DateTime periodUtc, decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
        var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();

        var documentId = await drafts.CreateDraftAsync(typeCode: "test", number: null, dateUtc: periodUtc, manageTransaction: true, ct: CancellationToken.None);

        await docs.PostAsync(
            documentId,
            postingAction: async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(documentId, periodUtc, chart.Get("50"), chart.Get("90.1"), amount);
            },
            ct: CancellationToken.None);

        return documentId;
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

    private static async Task<Outcome> RunRepostOutcomeAsync(
        IHost host,
        Guid documentId,
        DateTime periodUtc,
        TaskCompletionSource<bool> gate,
        decimal newAmount)
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

    private sealed record Outcome(Exception? Error)
    {
        public static Outcome Success() => new(Error: null);
        public static Outcome Fail(Exception ex) => new(Error: ex);
    }
}
