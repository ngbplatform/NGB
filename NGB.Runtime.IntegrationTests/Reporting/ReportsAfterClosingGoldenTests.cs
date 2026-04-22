using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.Reports.BalanceSheet;
using NGB.Persistence.Accounts;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Periods;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

/// <summary>
/// Reporting Core — golden tests that lock in reporting behavior around period/year closing.
/// IMPORTANT: Uses dedicated far-future fiscal year (2032) to avoid collisions with other tests.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ReportsAfterClosingGoldenTests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task BalanceSheet_AfterCloseFiscalYear_ExcludeNetIncomeInEquity_IsBalanced()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        // We post activity in Jan 2032, then close months Jan..Nov 2032 and perform FY close into Dec 2032 (which must be OPEN).
        var jan = new DateOnly(2032, 1, 1);
        var janUtc = Utc(jan);

        await SeedReportingCoAAsync(sp);

        // Owner injection (not P&L)
        await PostAsync(sp, Guid.CreateVersion7(), janUtc, debit: "50", credit: "80", amount: 1000m);

        // P&L activity (NetIncome = 300)
        await PostAsync(sp, Guid.CreateVersion7(), janUtc, debit: "91", credit: "60", amount: 200m);
        await PostAsync(sp, Guid.CreateVersion7(), janUtc, debit: "60", credit: "50", amount: 50m);
        await PostAsync(sp, Guid.CreateVersion7(), janUtc, debit: "50", credit: "90.1", amount: 500m);

        var closing = sp.GetRequiredService<IPeriodClosingService>();

        // Close months Jan..Nov (Dec must remain OPEN because FY closing posts into fiscalYearEndPeriod).
        for (var m = 1; m <= 11; m++)
            await closing.CloseMonthAsync(new DateOnly(2032, m, 1), closedBy: "tests");

        var retainedEarningsId = await GetAccountIdByCodeAsync(sp, "84");

        await closing.CloseFiscalYearAsync(
            fiscalYearEndPeriod: new DateOnly(2032, 12, 1),
            retainedEarningsAccountId: retainedEarningsId,
            closedBy: "tests");

        var bsReader = sp.GetRequiredService<IBalanceSheetReportReader>();

        var report = await bsReader.GetAsync(new BalanceSheetReportRequest
        {
            AsOfPeriod = new DateOnly(2032, 12, 1),
            IncludeNetIncomeInEquity = false,
            IncludeZeroAccounts = false
        });

        report.IsBalanced.Should().BeTrue();
        report.Difference.Should().Be(0m);

        report.TotalAssets.Should().Be(1450m);
        report.TotalLiabilities.Should().Be(150m);
        report.TotalEquity.Should().Be(1300m);
        report.TotalLiabilitiesAndEquity.Should().Be(1450m);
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
        await EnsureAsync(repo, mgmt, "84", "Retained earnings", AccountType.Equity, StatementSection.Equity);
        await EnsureAsync(repo, mgmt, "90.1", "Revenue", AccountType.Income, StatementSection.Income);
        await EnsureAsync(repo, mgmt, "91", "Expenses", AccountType.Expense, StatementSection.Expenses);
    }

    private static async Task<Guid> GetAccountIdByCodeAsync(IServiceProvider sp, string code)
    {
        var repo = sp.GetRequiredService<IChartOfAccountsRepository>();
        var all = await repo.GetForAdminAsync(includeDeleted: true);
        var item = all.FirstOrDefault(a => a.Account.Code == code)
                   ?? throw new XunitException($"Account not found in CoA: {code}");
        return item.Account.Id;
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
