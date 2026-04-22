using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.Reports.BalanceSheet;
using NGB.Persistence.Accounts;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

/// <summary>
/// Reporting Core — golden tests for Balance Sheet "synthetic Net Income" behavior.
/// These tests use far-future periods to avoid collisions with other tests that may close periods.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class BalanceSheetSyntheticNetIncomeGoldenTests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task BalanceSheet_IncludeNetIncomeInEquity_IsBalanced_BeforeClosing()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var period = new DateOnly(2033, 3, 1);
        var periodUtc = Utc(period);

        await SeedReportingCoAAsync(sp);

        // Owner injection (not part of P&L)
        await PostAsync(sp, Guid.CreateVersion7(), periodUtc, debit: "50", credit: "80", amount: 1000m);

        // P&L activity (NetIncome = 300)
        await PostAsync(sp, Guid.CreateVersion7(), periodUtc, debit: "91", credit: "60", amount: 200m);
        await PostAsync(sp, Guid.CreateVersion7(), periodUtc, debit: "60", credit: "50", amount: 50m);
        await PostAsync(sp, Guid.CreateVersion7(), periodUtc, debit: "50", credit: "90.1", amount: 500m);

        var reader = sp.GetRequiredService<IBalanceSheetReportReader>();

        var report = await reader.GetAsync(new BalanceSheetReportRequest
        {
            AsOfPeriod = period,
            IncludeNetIncomeInEquity = true,
            IncludeZeroAccounts = false
        });

        report.IsBalanced.Should().BeTrue();
        report.Difference.Should().Be(0m);

        // Economics:
        // Cash = 1450, AP = 150, Equity = Owner 1000 + NetIncome 300 = 1300
        report.TotalAssets.Should().Be(1450m);
        report.TotalLiabilities.Should().Be(150m);
        report.TotalEquity.Should().Be(1300m);
        report.TotalLiabilitiesAndEquity.Should().Be(1450m);
    }

    [Fact]
    public async Task BalanceSheet_WithoutClosedSnapshots_Includes_PreviousYear_History_In_AsOf_Balances()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var previousYearPeriod = new DateOnly(2034, 12, 1);
        var asOfPeriod = new DateOnly(2035, 1, 1);

        await SeedReportingCoAAsync(sp);

        // No month/year closing is performed in this scenario.
        // Balance Sheet must still be cumulative as-of and carry prior-year balances forward.
        await PostAsync(sp, Guid.CreateVersion7(), Utc(previousYearPeriod), debit: "50", credit: "90.1", amount: 400m);
        await PostAsync(sp, Guid.CreateVersion7(), Utc(asOfPeriod), debit: "50", credit: "90.1", amount: 50m);

        var reader = sp.GetRequiredService<IBalanceSheetReportReader>();

        var report = await reader.GetAsync(new BalanceSheetReportRequest
        {
            AsOfPeriod = asOfPeriod,
            IncludeNetIncomeInEquity = true,
            IncludeZeroAccounts = false
        });

        report.IsBalanced.Should().BeTrue();
        report.Difference.Should().Be(0m);
        report.TotalAssets.Should().Be(450m);
        report.TotalLiabilities.Should().Be(0m);
        report.TotalEquity.Should().Be(450m);
        report.TotalLiabilitiesAndEquity.Should().Be(450m);

        var assets = report.Sections.Single(s => s.Section == StatementSection.Assets);
        assets.Lines.Should().ContainSingle(l => l.AccountCode == "50" && l.Amount == 450m);

        var equity = report.Sections.Single(s => s.Section == StatementSection.Equity);
        equity.Lines.Should().ContainSingle(l => l.AccountCode == "NET" && l.AccountName == "Net Income" && l.Amount == 450m);
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
