using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.Reports.BalanceSheet;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Periods;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Periods;

/// <summary>
/// P0: Balance Sheet totals must be invariant across CloseFiscalYear.
/// Before closing, Balance Sheet can be balanced by including synthetic "Net Income" in Equity.
/// After closing, synthetic line should disappear and Retained Earnings should carry the same amount.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CloseFiscalYear_BalanceSheetTotalsInvariant_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task CloseFiscalYear_MovesNetIncomeFromSyntheticLineToRetainedEarnings_AndTotalsStayTheSame()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var endPeriod = new DateOnly(2026, 1, 1);
        var janUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var retainedEarningsId = await SeedCoaAsync(host);

        // Revenue 100, Expense 40 => Net income 60.
        await PostAsync(host, Guid.CreateVersion7(), janUtc, debit: "50", credit: "90.1", amount: 100m);
        await PostAsync(host, Guid.CreateVersion7(), janUtc, debit: "91", credit: "50", amount: 40m);

        // Ensure Retained Earnings is present in the Balance Sheet even before FY close.
        // (BalanceSheetReport is TB-driven and only includes accounts that had activity.)
        await PostAsync(host, Guid.CreateVersion7(), janUtc, debit: "50", credit: "300", amount: 1m);
        await PostAsync(host, Guid.CreateVersion7(), janUtc, debit: "300", credit: "50", amount: 1m);

        // BEFORE: P&L isn't closed yet.
        // Balance sheet is balanced only when we include synthetic "Net Income".
        var before = await GetBalanceSheetAsync(host.Services, endPeriod, includeNetIncomeInEquity: true, includeZeroAccounts: true);
        before.IsBalanced.Should().BeTrue();
        before.Difference.Should().Be(0m);

        var beforeEquity = before.Sections.Single(s => s.Section == StatementSection.Equity);
        beforeEquity.Lines.Should().ContainSingle(l => l.AccountCode == "NET" && l.AccountName == "Net Income" && l.Amount == 60m);
        beforeEquity.Lines.Should().ContainSingle(l => l.AccountCode == "300" && l.Amount == 0m, "Retained earnings must be empty before fiscal-year close");

        // ACT
        await CloseFiscalYearAsync(host, endPeriod, retainedEarningsId);

        // AFTER: P&L closed => no synthetic Net Income line required.
        var after = await GetBalanceSheetAsync(host.Services, endPeriod, includeNetIncomeInEquity: false, includeZeroAccounts: true);
        after.IsBalanced.Should().BeTrue();
        after.Difference.Should().Be(0m);

        var afterEquity = after.Sections.Single(s => s.Section == StatementSection.Equity);
        afterEquity.Lines.Should().NotContain(l => l.AccountCode == "NET");
        afterEquity.Lines.Should().ContainSingle(l => l.AccountCode == "300" && l.Amount == 60m);

        // Totals must remain invariant: Net income moved inside Equity (Retained Earnings), nothing else changes.
        after.TotalAssets.Should().Be(before.TotalAssets);
        after.TotalLiabilities.Should().Be(before.TotalLiabilities);
        after.TotalLiabilitiesAndEquity.Should().Be(before.TotalLiabilitiesAndEquity);

        // Even if we ask to include Net Income line after closing, it must not appear (P&L is zero now).
        var afterWithNetIncome = await GetBalanceSheetAsync(host.Services, endPeriod, includeNetIncomeInEquity: true, includeZeroAccounts: true);
        afterWithNetIncome.Sections.Single(s => s.Section == StatementSection.Equity)
            .Lines.Should().NotContain(l => l.AccountCode == "NET");
        afterWithNetIncome.TotalLiabilitiesAndEquity.Should().Be(after.TotalLiabilitiesAndEquity);
    }

    private static async Task<Guid> SeedCoaAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        // Balance sheet
        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "50",
            Name: "Cash",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets));

        var retainedEarningsId = await accounts.CreateAsync(new CreateAccountRequest(
            Code: "300",
            Name: "Retained Earnings",
            Type: AccountType.Equity,
            StatementSection: StatementSection.Equity));

        // P&L
        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "90.1",
            Name: "Revenue",
            Type: AccountType.Income,
            StatementSection: StatementSection.Income));

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "91",
            Name: "Expenses",
            Type: AccountType.Expense,
            StatementSection: StatementSection.Expenses));

        return retainedEarningsId;
    }

    private static async Task PostAsync(IHost host, Guid documentId, DateTime periodUtc, string debit, string credit, decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var engine = scope.ServiceProvider.GetRequiredService<PostingEngine>();

        await engine.PostAsync(
            operation: PostingOperation.Post,
            postingAction: async (ctx, ct) =>
            {
                var coa = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(documentId, periodUtc, coa.Get(debit), coa.Get(credit), amount);
            },
            manageTransaction: true,
            ct: CancellationToken.None);
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

    private static async Task<BalanceSheetReport> GetBalanceSheetAsync(
        IServiceProvider root,
        DateOnly asOfPeriod,
        bool includeNetIncomeInEquity,
        bool includeZeroAccounts)
    {
        await using var scope = root.CreateAsyncScope();
        var bs = scope.ServiceProvider.GetRequiredService<IBalanceSheetReportReader>();

        return await bs.GetAsync(new BalanceSheetReportRequest
        {
            AsOfPeriod = asOfPeriod,
            IncludeNetIncomeInEquity = includeNetIncomeInEquity,
            IncludeZeroAccounts = includeZeroAccounts
        }, CancellationToken.None);
    }
}
