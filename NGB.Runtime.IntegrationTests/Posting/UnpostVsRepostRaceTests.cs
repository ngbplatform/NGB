using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.Persistence.Locks;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.PostingState;
using NGB.Persistence.Readers.Reports;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.DependencyInjection;
using NGB.PostgreSql.Locks;
using NGB.Runtime.Accounts;
using NGB.Runtime.Documents;
using NGB.Runtime.Documents.Workflow;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Posting;

[Collection(PlatformPostgresCollection.Name)]
public sealed class UnpostVsRepostRaceTests(PostgresTestFixture fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnpostVsRepost_ConcurrentOperations_AreSerialized_NoDoubleStorno_NoDuplicatePostingLog(bool repostFirst)
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var periodUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var period = new DateOnly(2026, 1, 1);

        await SeedMinimalCoaAsync(host);

        Guid documentId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();

            documentId = await drafts.CreateDraftAsync(typeCode: "test", number: null, dateUtc: periodUtc, manageTransaction: true, ct: CancellationToken.None);

            // Original post: Cash D / Income C 100
            await docs.PostAsync(
                documentId,
                postingAction: async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    ctx.Post(documentId, periodUtc, chart.Get("50"), chart.Get("90.1"), 100m);
                },
                ct: CancellationToken.None);
        }

        var firstLocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resumeFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondAttempted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task PauseAfterLockAsync(Task acquisition)
        {
            await acquisition;
            firstLocked.TrySetResult();
            await resumeFirst.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }

        using var firstHost = IntegrationHostFactory.Create(fixture.ConnectionString, services =>
            services.AddScoped<IAdvisoryLockManager>(sp => ObservingProxy<IAdvisoryLockManager>.Wrap(
                CreateLockManager(sp),
                (method, _, result) => method.Name == nameof(IAdvisoryLockManager.LockDocumentAsync)
                    ? PauseAfterLockAsync((Task)result!)
                    : result)));
        using var secondHost = IntegrationHostFactory.Create(fixture.ConnectionString, services =>
            services.AddScoped<IAdvisoryLockManager>(sp => ObservingProxy<IAdvisoryLockManager>.Wrap(
                CreateLockManager(sp),
                (method, _, result) =>
                {
                    if (method.Name == nameof(IAdvisoryLockManager.LockDocumentAsync))
                        secondAttempted.TrySetResult();

                    return result;
                })));

        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        gate.SetResult(true);
        var first = repostFirst
            ? RunRepostAsync(firstHost, documentId, periodUtc, gate, newAmount: 200m)
            : RunUnpostAsync(firstHost, documentId, gate);
        Task<Outcome>? second = null;
        try
        {
            await firstLocked.Task.WaitAsync(TimeSpan.FromSeconds(10));
            second = repostFirst
                ? RunUnpostAsync(secondHost, documentId, gate)
                : RunRepostAsync(secondHost, documentId, periodUtc, gate, newAmount: 200m);
            await secondAttempted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            resumeFirst.TrySetResult();
        }

        var outcomes = await Task.WhenAll(first, second!).WaitAsync(TimeSpan.FromSeconds(30));

        // Both serialization orders are exercised explicitly, rather than left to scheduling.
        outcomes.Should().HaveCount(2);
        outcomes[0].Error.Should().BeNull();
        if (repostFirst)
            outcomes[1].Error.Should().BeNull();
        else
            outcomes[1].Error.Should().BeOfType<DocumentWorkflowStateMismatchException>();

        await using var verifyScope = host.Services.CreateAsyncScope();
        var sp = verifyScope.ServiceProvider;

        var docRepo = sp.GetRequiredService<IDocumentRepository>();
        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        var postingLog = sp.GetRequiredService<IPostingStateReader>();
        var trialBalance = sp.GetRequiredService<ITrialBalanceReader>();

        // 1) Document ends Draft (Unpost eventually applies).
        var doc = await docRepo.GetAsync(documentId, CancellationToken.None);
        doc.Should().NotBeNull();
        doc!.Status.Should().Be(DocumentStatus.Draft);

        // 2) No duplicates in posting_log per operation.
        var fromUtc = DateTime.UtcNow.AddHours(-2);
        var toUtc = DateTime.UtcNow.AddHours(2);

        var unpostLog = await postingLog.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = fromUtc,
            ToUtc = toUtc,
            DocumentId = documentId,
            Operation = PostingOperation.Unpost,
            PageSize = 20
        }, CancellationToken.None);

        unpostLog.Records.Should().HaveCount(1);
        unpostLog.Records.Single().CompletedAtUtc.Should().NotBeNull();

        var repostLog = await postingLog.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = fromUtc,
            ToUtc = toUtc,
            DocumentId = documentId,
            Operation = PostingOperation.Repost,
            PageSize = 20
        }, CancellationToken.None);

        // Unpost clears completed Post/Repost state to allow the next posting cycle.
        repostLog.Records.Should().BeEmpty();

        // 3) Reverse only the remaining stored effect. The original +100/-100 pair
        // must not be reversed again when Unpost follows a successful Repost to 200.
        var entries = await entryReader.GetByDocumentAsync(documentId, CancellationToken.None);

        entries.Should().HaveCount(repostFirst ? 4 : 2);
        entries.Count(e => e.IsStorno).Should().Be(repostFirst ? 2 : 1);
        var expectedCashAmounts = repostFirst ? new[] { 100m, -100m, 200m, -200m } : [100m, -100m];
        entries.OrderBy(entry => entry.EntryId)
            .Select(entry => entry.Debit.Code == "50" ? entry.Amount : -entry.Amount)
            .Should().Equal(expectedCashAmounts);

        // 4) Trial balance ends net-zero for the period in both cases.
        var tb = await trialBalance.GetAsync(period, period, CancellationToken.None);

        tb.Should().ContainSingle(r => r.AccountCode == "50" && r.ClosingBalance == 0m);
        tb.Should().ContainSingle(r => r.AccountCode == "90.1" && r.ClosingBalance == 0m);
    }

    private static PostgresAdvisoryLockManager CreateLockManager(IServiceProvider services)
        => new(
            services.GetRequiredService<IUnitOfWork>(),
            services.GetRequiredService<IOptions<PostgresOptions>>(),
            services.GetRequiredService<ILogger<PostgresAdvisoryLockManager>>(),
            TimeProvider.System);

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

    private static async Task<Outcome> RunUnpostAsync(IHost host, Guid documentId, TaskCompletionSource<bool> gate)
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

    private static async Task<Outcome> RunRepostAsync(
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

    private sealed record Outcome(bool Succeeded, Exception? Error)
    {
        public static Outcome Success() => new(true, null);
        public static Outcome Fail(Exception ex) => new(false, ex);
    }
}
