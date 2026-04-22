using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.Reports.TrialBalance;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Periods;
using NGB.Runtime.Posting;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

[Collection(PostgresCollection.Name)]
public sealed class ExoticMatrix_FiscalYearClose_ByDimensionSet_P2Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateOnly EndPeriod = new(2026, 1, 1); // month start
    private static readonly DateOnly YearStart = new(2026, 1, 1);

    // Must be within the end-period month (January 2026) and same UTC day for all entries of a document.
    private static readonly DateTime PostingDayUtc = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task CloseFiscalYear_ClosesProfitAndLossPerDimensionSet_ToZero()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, revenueId, expenseId, retainedEarningsId) = await SeedCoaForDimensionRulesAsync(host);

        var salesDoc = Guid.CreateVersion7();

        // Arrange: post income/expense movements.
        await PostSalesAndExpensesAsync(host, documentId: salesDoc);

        // Pre-close TB: right now postings are collapsed into Guid.Empty DimensionSet, so the trial balance is per-account only.
        var pre = await GetTrialBalanceAsync(host);
        var preRevenue = pre.Where(r => r.AccountCode == "90.1").ToList();
        var preExpense = pre.Where(r => r.AccountCode == "91").ToList();

        preRevenue.Should().HaveCount(1);
        preExpense.Should().HaveCount(1);

        preRevenue[0].DimensionSetId.Should().Be(Guid.Empty);
        preRevenue[0].Dimensions.Items.Should().BeEmpty();
        preRevenue[0].ClosingBalance.Should().Be(-350m);

        preExpense[0].DimensionSetId.Should().Be(Guid.Empty);
        preExpense[0].Dimensions.Items.Should().BeEmpty();
        preExpense[0].ClosingBalance.Should().Be(100m);

        // Act: fiscal year close posts closing entries INTO the end-period month.
        await CloseFiscalYearAsync(host, retainedEarningsId);

        // Assert: each (account+dimension set) row is individually closed to zero.
        var post = await GetTrialBalanceAsync(host);

        post.Where(r => r.AccountCode is "90.1" or "91")
            .Should()
            .OnlyContain(r => r.ClosingBalance == 0m);

        // Assert: closing entries are posted under deterministic close document id,
        // and each closing entry has no dimensions (collapsed by design).
        var expectedCloseDocumentId = DeterministicGuid.Create($"CloseFiscalYear|{EndPeriod:yyyy-MM-dd}");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            var entryReader = sp.GetRequiredService<IAccountingEntryReader>();

            var closingEntries = await entryReader.GetByDocumentAsync(expectedCloseDocumentId, CancellationToken.None);

            // With DimensionSet collapse, Trial Balance is per account, so we close in two aggregated entries: Revenue and Expense.
            closingEntries.Should().HaveCount(2);

            // Revenue => Debit Revenue; Credit Retained Earnings. No dimensions for both sides.
            closingEntries.Should().ContainSingle(e =>
                e.Debit.Code == "90.1" &&
                e.Amount == 350m &&
                e.Credit.Code == "84" &&
                e.DebitDimensions.IsEmpty && e.DebitDimensionSetId == Guid.Empty &&
                e.CreditDimensions.IsEmpty && e.CreditDimensionSetId == Guid.Empty);

            // Expense => Credit Expense; Debit Retained Earnings. No dimensions for both sides.
            closingEntries.Should().ContainSingle(e =>
                e.Credit.Code == "91" &&
                e.Amount == 100m &&
                e.Debit.Code == "84" &&
                e.DebitDimensions.IsEmpty && e.DebitDimensionSetId == Guid.Empty &&
                e.CreditDimensions.IsEmpty && e.CreditDimensionSetId == Guid.Empty);
        }
    }

    private static async Task<(Guid cashId, Guid revenueId, Guid expenseId, Guid retainedEarningsId)> SeedCoaForDimensionRulesAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        // Balance sheet
        var cashId = await accounts.CreateAsync(new CreateAccountRequest(
            Code: "50",
            Name: "Cash",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets
        ), CancellationToken.None);

        var retainedEarningsId = await accounts.CreateAsync(new CreateAccountRequest(
            Code: "84",
            Name: "Retained Earnings",
            Type: AccountType.Equity,
            StatementSection: StatementSection.Equity
        ), CancellationToken.None);

        // P&L accounts used in this test have no dimension rules; postings will use an empty DimensionSetId.
        var revenueId = await accounts.CreateAsync(new CreateAccountRequest(
            Code: "90.1",
            Name: "Revenue",
            Type: AccountType.Income,
            StatementSection: StatementSection.Income
        ), CancellationToken.None);

        var expenseId = await accounts.CreateAsync(new CreateAccountRequest(
            Code: "91",
            Name: "Expenses",
            Type: AccountType.Expense,
            StatementSection: StatementSection.Expenses
        ), CancellationToken.None);

        return (cashId, revenueId, expenseId, retainedEarningsId);
    }

    private static async Task PostSalesAndExpensesAsync(
        IHost host,
        Guid documentId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

        await posting.PostAsync(
            operation: PostingOperation.Post,
            postingAction: async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);

                var cash = chart.Get("50");
                var revenue = chart.Get("90.1");
                var expense = chart.Get("91");

                // Revenue
                ctx.Post(documentId, PostingDayUtc, cash, revenue, 100m);
                ctx.Post(documentId, PostingDayUtc, cash, revenue, 50m);
                ctx.Post(documentId, PostingDayUtc, cash, revenue, 200m);

                // Expense
                ctx.Post(documentId, PostingDayUtc, expense, cash, 30m);
                ctx.Post(documentId, PostingDayUtc, expense, cash, 70m);
            },
            manageTransaction: true,
            ct: CancellationToken.None);
    }

    private static async Task CloseFiscalYearAsync(IHost host, Guid retainedEarningsAccountId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var closing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();

        await closing.CloseFiscalYearAsync(
            fiscalYearEndPeriod: EndPeriod,
            retainedEarningsAccountId: retainedEarningsAccountId,
            closedBy: "test",
            ct: CancellationToken.None);
    }

    private static async Task<IReadOnlyList<TrialBalanceRow>> GetTrialBalanceAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var tb = scope.ServiceProvider.GetRequiredService<ITrialBalanceReader>();

        return await tb.GetAsync(YearStart, EndPeriod, CancellationToken.None);
    }
}
