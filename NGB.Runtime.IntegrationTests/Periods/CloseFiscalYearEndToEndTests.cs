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
public sealed class CloseFiscalYearEndToEndTests(PostgresTestFixture fixture)
{
    [Fact]
    public async Task CloseFiscalYearAsync_HappyPath_PostsClosingEntries_AndZerosOutProfitAndLoss()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var endPeriod = new DateOnly(2026, 1, 1); // month start
        var periodUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var retainedEarningsId = await SeedCoaForFiscalYearCloseAsync(host);

        // Create some P&L activity in the end period month.
        // Revenue 100 (credit-normal), Expense 40 (debit-normal) => Net income 60.
        await PostRevenueAsync(host, documentId: Guid.CreateVersion7(), periodUtc, amount: 100m);
        await PostExpenseAsync(host, documentId: Guid.CreateVersion7(), periodUtc, amount: 40m);

        var expectedCloseDocumentId = DeterministicGuid.Create($"CloseFiscalYear|{endPeriod:yyyy-MM-dd}");

        // Act
        await CloseFiscalYearAsync(host, endPeriod, retainedEarningsId);

        // Assert
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;

            var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
            var trialBalance = sp.GetRequiredService<ITrialBalanceReader>();
            var postingLog = sp.GetRequiredService<IPostingStateReader>();

            // 1) Closing entries posted under deterministic document id.
            var entries = await entryReader.GetByDocumentAsync(expectedCloseDocumentId, CancellationToken.None);
            entries.Should().HaveCount(2);

            entries.Should().ContainSingle(e =>
                e.Amount == 100m &&
                e.Debit.Code == "90.1" &&
                e.Credit.Code == "300");

            entries.Should().ContainSingle(e =>
                e.Amount == 40m &&
                e.Debit.Code == "300" &&
                e.Credit.Code == "91");

            // 2) Trial Balance for the end period month should show P&L accounts closed to zero.
            var tb = await trialBalance.GetAsync(endPeriod, endPeriod, CancellationToken.None);

            tb.Should().ContainSingle(r => r.AccountCode == "90.1" && r.ClosingBalance == 0m);
            tb.Should().ContainSingle(r => r.AccountCode == "91" && r.ClosingBalance == 0m);

            // Retained earnings should reflect net income.
            // Credit-normal Equity => ClosingBalance is negative when it has a credit balance.
            tb.Should().ContainSingle(r => r.AccountCode == "300" && r.ClosingBalance == -60m);

            // 3) Posting log has Completed record for CloseFiscalYear.
            var page = await postingLog.GetPageAsync(new PostingStatePageRequest
            {
                FromUtc = DateTime.UtcNow.AddHours(-1),
                ToUtc = DateTime.UtcNow.AddHours(1),
                DocumentId = expectedCloseDocumentId,
                Operation = PostingOperation.CloseFiscalYear,
                PageSize = 20
            }, CancellationToken.None);

            page.Records.Should().ContainSingle(r =>
                r.DocumentId == expectedCloseDocumentId &&
                r.Operation == PostingOperation.CloseFiscalYear &&
                r.Status == PostingStateStatus.Completed);
        }

        // 4) Double-close should be rejected (idempotency contract).
        var act = () => CloseFiscalYearAsync(host, endPeriod, retainedEarningsId);
        await act.Should().ThrowAsync<FiscalYearAlreadyClosedException>()
            .WithMessage($"*already closed*{expectedCloseDocumentId}*");
    }

    [Fact]
    public async Task CloseFiscalYearAsync_NoOp_RecordsPostingLogWithoutRegisterWrites()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var endPeriod = new DateOnly(2026, 1, 1);

        var retainedEarningsId = await SeedCoaForFiscalYearCloseAsync(host);

        var expectedCloseDocumentId = DeterministicGuid.Create($"CloseFiscalYear|{endPeriod:yyyy-MM-dd}");

        // Act
        await CloseFiscalYearAsync(host, endPeriod, retainedEarningsId);

        // Assert
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;

            var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
            var turnoverReader = sp.GetRequiredService<IAccountingTurnoverReader>();
            var postingLog = sp.GetRequiredService<IPostingStateReader>();

            var entries = await entryReader.GetByDocumentAsync(expectedCloseDocumentId, CancellationToken.None);
            entries.Should().BeEmpty();

            var turnovers = await turnoverReader.GetForPeriodAsync(endPeriod, CancellationToken.None);
            turnovers.Should().BeEmpty();

            var page = await postingLog.GetPageAsync(new PostingStatePageRequest
            {
                FromUtc = DateTime.UtcNow.AddHours(-1),
                ToUtc = DateTime.UtcNow.AddHours(1),
                DocumentId = expectedCloseDocumentId,
                Operation = PostingOperation.CloseFiscalYear,
                PageSize = 20
            }, CancellationToken.None);

            page.Records.Should().ContainSingle(r =>
                r.DocumentId == expectedCloseDocumentId &&
                r.Operation == PostingOperation.CloseFiscalYear &&
                r.Status == PostingStateStatus.Completed);
        }

        // Double-close should be rejected (already completed in posting_log).
        var act = () => CloseFiscalYearAsync(host, endPeriod, retainedEarningsId);
        await act.Should().ThrowAsync<FiscalYearAlreadyClosedException>()
            .WithMessage($"*already closed*{expectedCloseDocumentId}*");
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
            Name: "Retained Earnings",
            Type: AccountType.Equity,
            StatementSection: StatementSection.Equity,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        // P&L accounts
        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "90.1",
            Name: "Revenue",
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

    private static async Task PostRevenueAsync(IHost host, Guid documentId, DateTime periodUtc, decimal amount)
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
}
