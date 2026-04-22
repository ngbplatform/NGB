using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Persistence.Readers;
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
public sealed class CloseFiscalYearConcurrencyTests(PostgresTestFixture fixture)
{
    [Fact]
    public async Task CloseFiscalYearAsync_TwoConcurrentCalls_OneSucceeds_OtherFails_AndNoDuplicateClosingEntries()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var endPeriod = new DateOnly(2026, 1, 1); // month start
        var periodUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // IMPORTANT: retained earnings must be Equity (e.g. "300"), not a P&L account.
        var retainedEarningsId = await SeedCoaForFiscalYearCloseAsync(host);

        // Create some P&L activity in the end period month.
        // Income 100 (credit-normal), Expense 40 (debit-normal) => Net income 60.
        await PostIncomeAsync(host, documentId: Guid.CreateVersion7(), periodUtc, amount: 100m);
        await PostExpenseAsync(host, documentId: Guid.CreateVersion7(), periodUtc, amount: 40m);

        var expectedCloseDocumentId = DeterministicGuid.Create($"CloseFiscalYear|{endPeriod:yyyy-MM-dd}");

        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Act
        var tasks = new[]
        {
            RunCloseAsync(host, endPeriod, retainedEarningsId, gate),
            RunCloseAsync(host, endPeriod, retainedEarningsId, gate)
        };

        gate.SetResult(true);

        var outcomes = await Task.WhenAll(tasks)
            .WaitAsync(TimeSpan.FromSeconds(30));

        // Assert: exactly one succeeds
        outcomes.Count(o => o.Succeeded).Should().Be(1);
        outcomes.Count(o => !o.Succeeded).Should().Be(1);
        var failure = outcomes.Single(o => !o.Succeeded).Error;
        failure.Should().NotBeNull();
        var actualFailure = failure!;

        (actualFailure is FiscalYearAlreadyClosedException || actualFailure is FiscalYearClosingAlreadyInProgressException)
            .Should().BeTrue($"unexpected error: {actualFailure.GetType().Name} ({actualFailure.Message})");
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;

            var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
            var postingLog = sp.GetRequiredService<IPostingStateReader>();
            var trialBalance = sp.GetRequiredService<ITrialBalanceReader>();

            // 1) Closing entries exist exactly once under deterministic document id.
            var entries = await entryReader.GetByDocumentAsync(expectedCloseDocumentId, CancellationToken.None);
            entries.Should().HaveCount(2);

            // 2) Posting log has exactly one completed record for the deterministic document.
            var page = await postingLog.GetPageAsync(new PostingStatePageRequest
            {
                FromUtc = DateTime.UtcNow.AddHours(-1),
                ToUtc = DateTime.UtcNow.AddHours(1),
                DocumentId = expectedCloseDocumentId,
                Operation = PostingOperation.CloseFiscalYear,
                PageSize = 20
            }, CancellationToken.None);

            page.Records.Should().HaveCount(1);
            page.Records.Single().CompletedAtUtc.Should().NotBeNull();

            // 3) Trial Balance shows P&L accounts closed to zero (signals closing was applied exactly once).
            var tb = await trialBalance.GetAsync(endPeriod, endPeriod, CancellationToken.None);

            tb.Should().ContainSingle(r => r.AccountCode == "90.1" && r.ClosingBalance == 0m);
            tb.Should().ContainSingle(r => r.AccountCode == "91" && r.ClosingBalance == 0m);
        }
    }

    private static async Task<Outcome> RunCloseAsync(
        IHost host,
        DateOnly endPeriod,
        Guid retainedEarningsAccountId,
        TaskCompletionSource<bool> gate)
    {
        await gate.Task;

        try
        {
            await CloseFiscalYearAsync(host, endPeriod, retainedEarningsAccountId);
            return Outcome.Success();
        }
        catch (Exception ex)
        {
            return Outcome.Fail(ex);
        }
    }

    private static async Task<Guid> SeedCoaForFiscalYearCloseAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var accounts = sp.GetRequiredService<IChartOfAccountsManagementService>();

        // Balance sheet accounts
        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "50",
            Name: "Cash",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        // Retained Earnings (Equity, credit-normal)
        var retainedEarningsId = await accounts.CreateAsync(new CreateAccountRequest(
            Code: "300",
            Name: "Retained earnings",
            Type: AccountType.Equity,
            StatementSection: StatementSection.Equity,
            IsContra: false,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        // P&L accounts
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

    private static async Task CloseFiscalYearAsync(IHost host, DateOnly endPeriod, Guid retainedEarningsAccountId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var closing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();

        await closing.CloseFiscalYearAsync(
            fiscalYearEndPeriod: endPeriod,
            retainedEarningsAccountId: retainedEarningsAccountId,
            closedBy: "test",
            ct: CancellationToken.None);
    }

    private static async Task PostIncomeAsync(IHost host, Guid documentId, DateTime periodUtc, decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

        await posting.PostAsync(
            postingAction: async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                var debit = chart.Get("50");
                var credit = chart.Get("90.1");

                ctx.Post(documentId, periodUtc, debit, credit, amount);
            },
            ct: CancellationToken.None);
    }

    private static async Task PostExpenseAsync(IHost host, Guid documentId, DateTime periodUtc, decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

        await posting.PostAsync(
            postingAction: async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                var debit = chart.Get("91");
                var credit = chart.Get("50");

                ctx.Post(documentId, periodUtc, debit, credit, amount);
            },
            ct: CancellationToken.None);
    }

    private sealed record Outcome(bool Succeeded, Exception? Error)
    {
        public static Outcome Success() => new(true, null);
        public static Outcome Fail(Exception ex) => new(false, ex);
    }
}
