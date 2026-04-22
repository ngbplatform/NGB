using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.Reports.BalanceSheet;
using NGB.Accounting.Reports.GeneralJournal;
using NGB.Persistence.Accounts;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

/// <summary>
/// Reporting Core - additional golden tests focused on business semantics:
/// - Balance Sheet balancing behavior with/without synthetic Net Income line
/// - General Journal page contract
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class BalanceSheetSemanticsGoldenTests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task BalanceSheet_ExcludeNetIncomeInEquity_IsNotBalanced_DifferenceEqualsNetIncome()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var period = new DateOnly(2026, 1, 1);
        var periodUtc = Utc(period);

        await SeedReportingCoAAsync(sp);

        // Owner injection (not part of P&L):
        await PostAsync(sp, Guid.CreateVersion7(), periodUtc, debit: "50", credit: "80", amount: 1000m);

        // P&L activity:
        await PostAsync(sp, Guid.CreateVersion7(), periodUtc, debit: "91", credit: "60", amount: 200m);    // expense on credit
        await PostAsync(sp, Guid.CreateVersion7(), periodUtc, debit: "60", credit: "50", amount: 50m);     // pay AP
        await PostAsync(sp, Guid.CreateVersion7(), periodUtc, debit: "50", credit: "90.1", amount: 500m);  // revenue

        var reader = sp.GetRequiredService<IBalanceSheetReportReader>();

        var report = await reader.GetAsync(new BalanceSheetReportRequest
        {
            AsOfPeriod = period,
            IncludeNetIncomeInEquity = false,
            IncludeZeroAccounts = false
        });

        // With IncludeNetIncomeInEquity=false, the Balance Sheet is allowed to be unbalanced
        // until P&L is closed to retained earnings.
        report.IsBalanced.Should().BeFalse();

        // In this scenario NetIncome = 500 - 200 = 300.
        // Without synthetic Net Income in Equity, the difference equals that amount.
        report.Difference.Should().Be(300m);

        report.TotalAssets.Should().Be(1450m);                 // Cash
        report.TotalLiabilities.Should().Be(150m);             // AP
        report.TotalEquity.Should().Be(1000m);                 // Owner's equity only
        report.TotalLiabilitiesAndEquity.Should().Be(1150m);   // 150 + 1000
    }

    [Fact]
    public async Task GeneralJournalReport_LinesSum_ToExpectedAmount()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var period = new DateOnly(2026, 2, 1);
        var periodUtc = Utc(period);

        await SeedReportingCoAAsync(sp);

        // 3 journal lines in the same month
        await PostAsync(sp, Guid.CreateVersion7(), periodUtc, debit: "50", credit: "80", amount: 1000m);
        await PostAsync(sp, Guid.CreateVersion7(), periodUtc, debit: "91", credit: "60", amount: 200m);
        await PostAsync(sp, Guid.CreateVersion7(), periodUtc, debit: "50", credit: "90.1", amount: 500m);

        var reportReader = sp.GetRequiredService<IGeneralJournalReportReader>();

        var page = await reportReader.GetPageAsync(new GeneralJournalPageRequest
        {
            FromInclusive = period,
            ToInclusive = period,
            PageSize = 100,
            Cursor = null
        });

        page.Lines.Should().HaveCount(3);

        var sum = page.Lines.Sum(l => l.Amount);

        sum.Should().Be(1700m);
    }

    private static DateTime Utc(DateOnly periodMonthStart) =>
        new(periodMonthStart.Year, periodMonthStart.Month, periodMonthStart.Day, 0, 0, 0, DateTimeKind.Utc);

    private static async Task SeedReportingCoAAsync(IServiceProvider sp)
    {
        var repo = sp.GetRequiredService<IChartOfAccountsRepository>();
        var mgmt = sp.GetRequiredService<IChartOfAccountsManagementService>();

        static async Task EnsureAsync(
            IChartOfAccountsRepository repo,
            IChartOfAccountsManagementService mgmt,
            string code,
            string name,
            AccountType type,
            StatementSection section)
        {
            var all = await repo.GetForAdminAsync(includeDeleted: true);
            var existing = all.FirstOrDefault(a => a.Account.Code == code);

            if (existing is null)
            {
                var id = await mgmt.CreateAsync(new CreateAccountRequest(
                    Code: code,
                    Name: name,
                    Type: type,
                    StatementSection: section,
                    IsContra: false));

                // default is Active in current impl, but keep it explicit for tests
                await mgmt.SetActiveAsync(id, true);
                return;
            }

            if (!existing.IsActive)
                await mgmt.SetActiveAsync(existing.Account.Id, true);
        }

        await EnsureAsync(repo, mgmt, "50", "Cash", AccountType.Asset, StatementSection.Assets);
        await EnsureAsync(repo, mgmt, "60", "Accounts Payable", AccountType.Liability, StatementSection.Liabilities);
        await EnsureAsync(repo, mgmt, "80", "Owner's Equity", AccountType.Equity, StatementSection.Equity);
        await EnsureAsync(repo, mgmt, "90.1", "Revenue", AccountType.Income, StatementSection.Income);
        await EnsureAsync(repo, mgmt, "91", "Expenses", AccountType.Expense, StatementSection.Expenses);
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
            manageTransaction: true);
    }
}
