using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Accounting.Turnovers;
using NGB.BackgroundJobs.Jobs;
using NGB.Core.Documents;
using NGB.Persistence.Checkers;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.Periods;
using NGB.Persistence.Readers.PostingState;
using NGB.PostgreSql.Readers;
using NGB.Runtime.Accounts;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Periods;
using Xunit;
using NGB.Runtime.Posting;

namespace NGB.Runtime.IntegrationTests.Concurrency;

[Collection(PostgresCollection.Name)]
public sealed class CloseMonth_Posting_BackgroundJobs_Concurrency_P0Tests(PostgresTestFixture fixture)
{
    [Fact]
    public async Task CloseMonth_PostAsync_IntegrityScanJob_Concurrent_SamePeriod_NoDeadlock_MonthClosed_JobSucceeds_PostRejectedWithoutSideEffects()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();

        var closeMonthEnteredTurnovers = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowCloseMonthToContinue = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var host = IntegrationHostFactory.Create(
            fixture.ConnectionString,
            configureTestServices: services =>
            {
                // Deterministic overlap:
                // CloseMonth acquires the period lock and then reads turnovers. We pause at that point,
                // so concurrent PostAsync must wait for the period lock, while the job reads the ledger.
                services.RemoveAll<IAccountingTurnoverReader>();
                services.AddScoped<PostgresAccountingTurnoverReader>();
                services.AddScoped<IAccountingTurnoverReader>(sp =>
                    new GateTurnoverReader(
                        sp.GetRequiredService<PostgresAccountingTurnoverReader>(),
                        closeMonthEnteredTurnovers,
                        allowCloseMonthToContinue));
            });

        var nowUtc = DateTime.UtcNow;
        var period = new DateOnly(nowUtc.Year, nowUtc.Month, 1);
        var periodUtc = new DateTime(period.Year, period.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        await SeedMinimalCoaAsync(host);

        // Seed at least one posted doc so CloseMonth has real turnovers to read.
        _ = await CreateAndPostAsync(host, periodUtc, amount: 100m);

        var draftId = await CreateDraftAsync(host, typeCode: "test", dateUtc: periodUtc);

        // Act
        var closeTask = Task.Run(async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var closing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();
            await closing.CloseMonthAsync(period, closedBy: "test", ct: CancellationToken.None);
        });

        // Wait until CloseMonth entered the transaction, acquired the period lock, and is about to read turnovers.
        await closeMonthEnteredTurnovers.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var postTask = Task.Run(async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

            await docs.PostAsync(
                draftId,
                postingAction: async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    ctx.Post(draftId, periodUtc, chart.Get("50"), chart.Get("90.1"), amount: 1m);
                },
                ct: CancellationToken.None);
        });

        var jobTask = Task.Run(async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var checker = scope.ServiceProvider.GetRequiredService<IAccountingIntegrityChecker>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<AccountingIntegrityScanJob>>();

            var metrics = new TestJobRunMetrics();
            var job = new AccountingIntegrityScanJob(checker, logger, metrics);
            await job.RunAsync(CancellationToken.None);
            return metrics.Snapshot();
        });

        // Ensure the job truly runs while CloseMonth still holds the period lock.
        var jobCounters = await jobTask.WaitAsync(TimeSpan.FromSeconds(45));

        // Unblock CloseMonth and let it finish closing the month.
        allowCloseMonthToContinue.TrySetResult(true);

        await closeTask.WaitAsync(TimeSpan.FromSeconds(45));

        Exception? postError = null;
        try
        {
            await postTask.WaitAsync(TimeSpan.FromSeconds(45));
        }
        catch (Exception ex)
        {
            postError = ex;
        }

        // Assert: job succeeded
        jobCounters.Should().ContainKey("periods_total");
        jobCounters["periods_total"].Should().Be(2);
        jobCounters.Should().ContainKey("periods_scanned");
        jobCounters["periods_scanned"].Should().Be(2);

        // Assert: PostAsync must be rejected due to closed period (since CloseMonth held the period lock first)
        postError.Should().NotBeNull();
        postError.Should().BeOfType<PostingPeriodClosedException>();
        postError!.Message.ToLowerInvariant().Should().Contain("closed");

        await using var verifyScope = host.Services.CreateAsyncScope();
        var sp = verifyScope.ServiceProvider;

        // Month must be closed.
        var closedReader = sp.GetRequiredService<IClosedPeriodReader>();
        (await closedReader.GetClosedAsync(period, period, CancellationToken.None))
            .Should().ContainSingle(x => x.Period == period);

        // No side effects for the rejected posting attempt.
        var docRepo = sp.GetRequiredService<NGB.Persistence.Documents.IDocumentRepository>();
        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        var postingLog = sp.GetRequiredService<IPostingStateReader>();

        var draft = await docRepo.GetAsync(draftId, CancellationToken.None);
        draft.Should().NotBeNull();
        draft!.Status.Should().Be(DocumentStatus.Draft);

        (await entryReader.GetByDocumentAsync(draftId, CancellationToken.None))
            .Should().BeEmpty();

        var fromUtc = DateTime.UtcNow.AddHours(-2);
        var toUtc = DateTime.UtcNow.AddHours(2);

        var logPage = await postingLog.GetPageAsync(new PostingStatePageRequest
        {
            DocumentId = draftId,
            Operation = PostingOperation.Post,
            FromUtc = fromUtc,
            ToUtc = toUtc,
            PageSize = 20
        }, CancellationToken.None);

        logPage.Records.Should().BeEmpty();
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

    private static async Task<Guid> CreateDraftAsync(IHost host, string typeCode, DateTime dateUtc)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
        return await drafts.CreateDraftAsync(typeCode, number: null, dateUtc, manageTransaction: true, ct: CancellationToken.None);
    }

    private static async Task<Guid> CreateAndPostAsync(IHost host, DateTime periodUtc, decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

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

    private sealed class GateTurnoverReader(
        IAccountingTurnoverReader inner,
        TaskCompletionSource<bool> entered,
        TaskCompletionSource<bool> allowContinue) : IAccountingTurnoverReader
    {
        public async Task<IReadOnlyList<AccountingTurnover>> GetForPeriodAsync(DateOnly period, CancellationToken ct = default)
        {
            entered.TrySetResult(true);
            await allowContinue.Task;
            return await inner.GetForPeriodAsync(period, ct);
        }

        public Task<IReadOnlyList<AccountingTurnover>> GetRangeAsync(
            DateOnly fromInclusive,
            DateOnly toInclusive,
            CancellationToken ct = default) =>
            inner.GetRangeAsync(fromInclusive, toInclusive, ct);
    }

    private sealed class TestJobRunMetrics : BackgroundJobs.Contracts.IJobRunMetrics
    {
        private readonly Dictionary<string, long> _counters = new(StringComparer.Ordinal);

        public void Increment(string name, long delta = 1)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;
            if (delta == 0)
                return;

            var key = name.Trim();
            if (_counters.TryGetValue(key, out var current))
                _counters[key] = current + delta;
            else
                _counters[key] = delta;
        }

        public void Set(string name, long value)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;

            _counters[name.Trim()] = value;
        }

        public IReadOnlyDictionary<string, long> Snapshot() => new Dictionary<string, long>(_counters);
    }
}
