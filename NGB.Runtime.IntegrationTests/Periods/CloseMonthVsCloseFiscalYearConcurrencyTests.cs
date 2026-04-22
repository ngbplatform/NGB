using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.Periods;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.Periods;
using NGB.Persistence.Readers.PostingState;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Periods;
using NGB.Runtime.Posting;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Periods;

[Collection(PostgresCollection.Name)]
public sealed class CloseMonthVsCloseFiscalYearConcurrencyTests(PostgresTestFixture fixture)
{
    [Fact]
    public async Task CloseMonthVsCloseFiscalYear_ConcurrentOnLastMonth_NoDeadlock_EndMonthClosed_AndYearCloseAtMostOnce()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        // FY2025 last month
        var fiscalYearEndPeriod = new DateOnly(2025, 12, 1);
        var decemberUtc = new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc);

        var retainedEarningsId = await SeedCoaForFiscalYearCloseAsync(host);

        // Close months Jan..Nov (required by FY close contract)
        for (var m = 1; m <= 11; m++)
        {
            await CloseMonthAsync(host, new DateOnly(2025, m, 1));
        }

        // Create P&L activity in December so FY close is not a no-op.
        await PostIncomeAsync(host, Guid.CreateVersion7(), decemberUtc, 100m);
        await PostExpenseAsync(host, Guid.CreateVersion7(), decemberUtc, 40m);

        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var closeMonthTask = RunCloseMonthAsync(host, fiscalYearEndPeriod, gate);
        var closeYearTask = RunCloseFiscalYearAsync(host, fiscalYearEndPeriod, retainedEarningsId, gate);

        gate.SetResult(true);

        var outcomes = await Task.WhenAll(closeMonthTask, closeYearTask)
            .WaitAsync(TimeSpan.FromSeconds(45));

        var closeMonthOutcome = outcomes.Single(o => o.Kind == "CloseMonth");
        var closeYearOutcome = outcomes.Single(o => o.Kind == "CloseFiscalYear");

        // CloseMonth must succeed or be "already closed" (race).
        if (closeMonthOutcome.Error is not null)
            closeMonthOutcome.Error.Should().BeOfType<PeriodAlreadyClosedException>();

        // CloseFiscalYear: in race it may succeed, or fail for a legitimate reason depending on ordering.
        if (closeYearOutcome.Error is not null)
        {
            var e = closeYearOutcome.Error;

            (e is FiscalYearClosingPrerequisiteNotMetException ||
             e is FiscalYearClosingAlreadyInProgressException ||
             e is FiscalYearAlreadyClosedException ||
             e is PeriodAlreadyClosedException)
                .Should().BeTrue($"unexpected error: {e.GetType().Name} ({e.Message})");
        }

        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var closedReader = sp.GetRequiredService<IClosedPeriodReader>();
        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        var postingLog = sp.GetRequiredService<IPostingStateReader>();
        var trialBalance = sp.GetRequiredService<ITrialBalanceReader>();

        // End month must be closed
        var closed = await closedReader.GetClosedAsync(fiscalYearEndPeriod, fiscalYearEndPeriod, CancellationToken.None);
        closed.Should().ContainSingle(p => p.Period == fiscalYearEndPeriod);

        // Year close is deterministic document id (per current implementation).
        var closeDocumentId = DeterministicGuid.Create($"CloseFiscalYear|{fiscalYearEndPeriod:yyyy-MM-dd}");

        // Posting log for FY close: at most one record, and if present must be completed.
        var logPage = await postingLog.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = DateTime.UtcNow.AddHours(-4),
            ToUtc = DateTime.UtcNow.AddHours(4),
            DocumentId = closeDocumentId,
            Operation = PostingOperation.CloseFiscalYear,
            PageSize = 20
        }, CancellationToken.None);

        logPage.Records.Count.Should().BeLessThanOrEqualTo(1);
        if (logPage.Records.Count == 1)
            logPage.Records.Single().CompletedAtUtc.Should().NotBeNull();

        // If FY close completed -> closing entries should exist exactly once and P&L accounts are closed to zero.
        if (logPage.Records.Count == 1 && logPage.Records.Single().CompletedAtUtc is not null)
        {
            var closingEntries = await entryReader.GetByDocumentAsync(closeDocumentId, CancellationToken.None);
            closingEntries.Should().HaveCount(2);

            var tb = await trialBalance.GetAsync(fiscalYearEndPeriod, fiscalYearEndPeriod, CancellationToken.None);
            tb.Should().ContainSingle(r => r.AccountCode == "90.1" && r.ClosingBalance == 0m);
            tb.Should().ContainSingle(r => r.AccountCode == "91" && r.ClosingBalance == 0m);
        }
    }

    private static async Task<Guid> SeedCoaForFiscalYearCloseAsync(IHost host)
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

        var retainedEarningsId = await accounts.CreateAsync(new CreateAccountRequest(
            Code: "300",
            Name: "Retained earnings",
            Type: AccountType.Equity,
            StatementSection: StatementSection.Equity,
            IsContra: false,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "90.1",
            Name: "Income",
            Type: AccountType.Income,
            StatementSection: StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "91",
            Name: "Expenses",
            Type: AccountType.Expense,
            StatementSection: StatementSection.Expenses,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        return retainedEarningsId;
    }

    private static async Task PostIncomeAsync(IHost host, Guid documentId, DateTime periodUtc, decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<NGB.Runtime.Posting.PostingEngine>();

        await posting.PostAsync(
            postingAction: async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(documentId, periodUtc, chart.Get("50"), chart.Get("90.1"), amount);
            },
            ct: CancellationToken.None);
    }

    private static async Task PostExpenseAsync(IHost host, Guid documentId, DateTime periodUtc, decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<NGB.Runtime.Posting.PostingEngine>();

        await posting.PostAsync(
            postingAction: async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(documentId, periodUtc, chart.Get("91"), chart.Get("50"), amount);
            },
            ct: CancellationToken.None);
    }

    private static async Task CloseMonthAsync(IHost host, DateOnly period)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var closing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();
        await closing.CloseMonthAsync(period, closedBy: "test", ct: CancellationToken.None);
    }

    private static async Task<Outcome> RunCloseMonthAsync(IHost host, DateOnly period, TaskCompletionSource<bool> gate)
    {
        await gate.Task;
        try
        {
            await CloseMonthAsync(host, period);
            return Outcome.Ok("CloseMonth");
        }
        catch (Exception ex)
        {
            return Outcome.Fail("CloseMonth", ex);
        }
    }

    private static async Task<Outcome> RunCloseFiscalYearAsync(IHost host, DateOnly period, Guid retainedEarningsId, TaskCompletionSource<bool> gate)
    {
        await gate.Task;
        try
        {
            await using var scope = host.Services.CreateAsyncScope();
            var closing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();
            await closing.CloseFiscalYearAsync(period, retainedEarningsId, closedBy: "test", ct: CancellationToken.None);
            return Outcome.Ok("CloseFiscalYear");
        }
        catch (Exception ex)
        {
            return Outcome.Fail("CloseFiscalYear", ex);
        }
    }

    private sealed record Outcome(string Kind, Exception? Error)
    {
        public static Outcome Ok(string kind) => new(kind, null);
        public static Outcome Fail(string kind, Exception ex) => new(kind, ex);
    }
}
