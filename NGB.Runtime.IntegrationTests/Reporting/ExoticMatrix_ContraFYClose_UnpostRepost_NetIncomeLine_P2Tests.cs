using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Accounting.Reports.BalanceSheet;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.PostingState;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Periods;
using NGB.Runtime.Posting;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

/// <summary>
/// "Exotic" integration coverage: Contra P&L + Fiscal Year Close + Balance Sheet NET line behavior +
/// operational contract for Unpost/Repost after closing.
///
/// Why it matters:
/// - Contra P&L accounts invert the normal balance and can break FY closing if the closing direction
///   is derived from StatementSection only.
/// - After FY close, P&L balances become 0 => BalanceSheet must NOT show a synthetic "Net Income" line
///   even when IncludeNetIncomeInEquity=true.
/// - Closed-period policy must keep Unpost/Repost forbidden and side-effect free.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ExoticMatrix_ContraFYClose_UnpostRepost_NetIncomeLine_P2Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task CloseFiscalYear_ClosesContraProfitAndLoss_ToZero_AndBalanceSheetHasNoNetIncomeLine()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var year = 2040;
        var jan = new DateOnly(year, 1, 1);
        var dec = new DateOnly(year, 12, 1);
        var janUtc = Utc(jan);

        var (retainedEarningsId, docs) = await SeedAndPostScenarioAsync(sp, janUtc);

        // Close Jan..Nov (Dec must remain OPEN because FY closing posts into Dec).
        var closing = sp.GetRequiredService<IPeriodClosingService>();
        for (var m = 1; m <= 11; m++)
            await closing.CloseMonthAsync(new DateOnly(year, m, 1), closedBy: "tests");

        await closing.CloseFiscalYearAsync(
            fiscalYearEndPeriod: dec,
            retainedEarningsAccountId: retainedEarningsId,
            closedBy: "tests");

        var expectedCloseDocumentId = DeterministicGuid.Create($"CloseFiscalYear|{dec:yyyy-MM-dd}");

        // 1) Closing entries: direction must be derived from signed ClosingBalance (handles contra P&L).
        var entries = await sp.GetRequiredService<IAccountingEntryReader>()
            .GetByDocumentAsync(expectedCloseDocumentId, CancellationToken.None);

        entries.Should().HaveCount(4);

        // Revenue (credit-normal): net CREDIT => Debit Revenue, Credit Retained Earnings.
        entries.Should().ContainSingle(e =>
            e.Amount == 200m && e.Debit.Code == "90.1" && e.Credit.Code == "84");

        // Contra income (debit-normal): net DEBIT => Credit Contra account, Debit Retained Earnings.
        entries.Should().ContainSingle(e =>
            e.Amount == 20m && e.Debit.Code == "84" && e.Credit.Code == "90.2");

        // Expense (debit-normal): net DEBIT => Credit Expense, Debit Retained Earnings.
        entries.Should().ContainSingle(e =>
            e.Amount == 80m && e.Debit.Code == "84" && e.Credit.Code == "91");

        // Contra expense (credit-normal): net CREDIT => Debit Contra account, Credit Retained Earnings.
        entries.Should().ContainSingle(e =>
            e.Amount == 10m && e.Debit.Code == "91.1" && e.Credit.Code == "84");

        // 2) Trial Balance: all P&L accounts are closed to ZERO as of Dec.
        var tb = await sp.GetRequiredService<ITrialBalanceReader>()
            .GetAsync(new DateOnly(year, 1, 1), dec, CancellationToken.None);

        tb.Should().ContainSingle(r => r.AccountCode == "90.1" && r.ClosingBalance == 0m);
        tb.Should().ContainSingle(r => r.AccountCode == "90.2" && r.ClosingBalance == 0m);
        tb.Should().ContainSingle(r => r.AccountCode == "91" && r.ClosingBalance == 0m);
        tb.Should().ContainSingle(r => r.AccountCode == "91.1" && r.ClosingBalance == 0m);

        // Net income: 200 - 20 - 80 + 10 = 110.
        // Retained earnings is Equity (credit-normal) => credit balance shows as negative ClosingBalance.
        tb.Should().ContainSingle(r => r.AccountCode == "84" && r.ClosingBalance == -110m);

        // 3) Balance Sheet: after FY close there must be NO synthetic NET line even when flag is enabled.
        var bs = await sp.GetRequiredService<IBalanceSheetReportReader>()
            .GetAsync(new BalanceSheetReportRequest
            {
                AsOfPeriod = dec,
                IncludeZeroAccounts = false,
                IncludeNetIncomeInEquity = true
            }, CancellationToken.None);

        bs.IsBalanced.Should().BeTrue();
        bs.Difference.Should().Be(0m);

        var equity = bs.Sections.Single(s => s.Section == StatementSection.Equity);
        equity.Lines.Should().NotContain(l => l.AccountCode == "NET", "P&L accounts are closed to zero, so synthetic Net Income must disappear after FY close");

        bs.TotalAssets.Should().Be(1110m);
        bs.TotalLiabilities.Should().Be(0m);
        bs.TotalEquity.Should().Be(1110m);
        bs.TotalLiabilitiesAndEquity.Should().Be(1110m);

        // 4) Operational contract: after period close, Unpost/Repost are forbidden (no side effects).
        var logWindow = PostingLogTestWindow.Capture();
        var postingLog = sp.GetRequiredService<IPostingStateReader>();

        var unposting = sp.GetRequiredService<UnpostingService>();
        var actUnpost = () => unposting.UnpostAsync(docs.RevenueDocId, CancellationToken.None);

        await actUnpost.Should().ThrowAsync<PostingPeriodClosedException>()
            .WithMessage("*Posting is forbidden. Period is closed: 2040-01-01*");

        var unpostLog = await postingLog.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = logWindow.FromUtc,
            ToUtc = logWindow.ToUtc,
            DocumentId = docs.RevenueDocId,
            Operation = PostingOperation.Unpost,
            PageSize = 100
        }, CancellationToken.None);

        unpostLog.Records.Should().BeEmpty("forbidden operations must NOT create posting_log records");

        var reposting = sp.GetRequiredService<RepostingService>();
        var actRepost = () => reposting.RepostAsync(
            docs.RevenueDocId,
            postNew: async (ctx, ct) =>
            {
                var coa = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(docs.RevenueDocId, janUtc, coa.Get("50"), coa.Get("90.1"), 999m);
            },
            ct: CancellationToken.None);

        await actRepost.Should().ThrowAsync<PostingPeriodClosedException>()
            .WithMessage("*Posting is forbidden. Period is closed: 2040-01-01*");
    }

    private static DateTime Utc(DateOnly periodMonthStart) =>
        new(periodMonthStart.Year, periodMonthStart.Month, periodMonthStart.Day, 0, 0, 0, DateTimeKind.Utc);

    private sealed record DocIds(Guid RevenueDocId);

    private static async Task<(Guid retainedEarningsId, DocIds docs)> SeedAndPostScenarioAsync(
        IServiceProvider sp,
        DateTime janUtc)
    {
        var accounts = sp.GetRequiredService<IChartOfAccountsManagementService>();

        // Balance sheet
        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "50",
            Name: "Cash",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow),
            CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "80",
            Name: "Owner's Equity",
            Type: AccountType.Equity,
            StatementSection: StatementSection.Equity,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow),
            CancellationToken.None);

        var retainedEarningsId = await accounts.CreateAsync(new CreateAccountRequest(
            Code: "84",
            Name: "Retained Earnings",
            Type: AccountType.Equity,
            StatementSection: StatementSection.Equity,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow),
            CancellationToken.None);

        // P&L (with contra)
        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "90.1",
            Name: "Revenue",
            Type: AccountType.Income,
            StatementSection: StatementSection.Income,
            IsContra: false,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow),
            CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "90.2",
            Name: "Sales Returns (Contra Revenue)",
            Type: AccountType.Income,
            StatementSection: StatementSection.Income,
            IsContra: true,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow),
            CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "91",
            Name: "Expenses",
            Type: AccountType.Expense,
            StatementSection: StatementSection.Expenses,
            IsContra: false,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow),
            CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "91.1",
            Name: "Purchase Discounts (Contra Expense)",
            Type: AccountType.Expense,
            StatementSection: StatementSection.Expenses,
            IsContra: true,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow),
            CancellationToken.None);

        // Post transactions.
        var ownerDoc = Guid.CreateVersion7();
        await PostAsync(sp, ownerDoc, janUtc, debit: "50", credit: "80", amount: 1000m);

        var revenueDoc = Guid.CreateVersion7();
        await PostAsync(sp, revenueDoc, janUtc, debit: "50", credit: "90.1", amount: 200m);

        var expenseDoc = Guid.CreateVersion7();
        await PostAsync(sp, expenseDoc, janUtc, debit: "91", credit: "50", amount: 80m);

        var returnsDoc = Guid.CreateVersion7();
        await PostAsync(sp, returnsDoc, janUtc, debit: "90.2", credit: "50", amount: 20m);

        var discountDoc = Guid.CreateVersion7();
        await PostAsync(sp, discountDoc, janUtc, debit: "50", credit: "91.1", amount: 10m);

        return (retainedEarningsId, new DocIds(revenueDoc));
    }

    private static async Task PostAsync(
        IServiceProvider sp,
        Guid documentId,
        DateTime periodUtc,
        string debit,
        string credit,
        decimal amount)
    {
        var engine = sp.GetRequiredService<PostingEngine>();

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
}
