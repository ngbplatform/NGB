using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.Reports.BalanceSheet;
using NGB.Accounting.Reports.IncomeStatement;
using NGB.Persistence.Accounts;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

[Collection(PostgresCollection.Name)]
public sealed class ContraAccounts_StatementSemantics_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateTime PeriodUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Period = DateOnly.FromDateTime(PeriodUtc);

    [Fact]
    public async Task BalanceSheet_And_IncomeStatement_PresentContraBalancesWithCorrectSign()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await SeedCoAAsync(host);

        // Owner investment: Cash +1000 / Equity +1000
        await PostAsync(host, Guid.CreateVersion7(), PeriodUtc, debitCode: "50", creditCode: "80", amount: 1000m);

        // Sale in cash: Cash +300 / Revenue +300
        await PostAsync(host, Guid.CreateVersion7(), PeriodUtc, debitCode: "50", creditCode: "90.1", amount: 300m);

        // Return/refund: Sales Returns (contra revenue) +50 / Cash -50
        await PostAsync(host, Guid.CreateVersion7(), PeriodUtc, debitCode: "90.2", creditCode: "50", amount: 50m);

        // Purchase equipment: Equipment +400 / Cash -400
        await PostAsync(host, Guid.CreateVersion7(), PeriodUtc, debitCode: "01", creditCode: "50", amount: 400m);

        // Depreciation: Depreciation expense +100 / Accumulated depreciation (contra asset) +100
        await PostAsync(host, Guid.CreateVersion7(), PeriodUtc, debitCode: "91", creditCode: "02", amount: 100m);

        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var income = await sp.GetRequiredService<IIncomeStatementReportReader>()
            .GetAsync(
                new IncomeStatementReportRequest
                {
                    FromInclusive = Period,
                    ToInclusive = Period,
                    IncludeZeroLines = false
                },
                CancellationToken.None);

        income.TotalIncome.Should().Be(250m);   // 300 - 50
        income.TotalExpenses.Should().Be(100m);
        income.NetIncome.Should().Be(150m);

        var incomeLines = income.Sections.SelectMany(s => s.Lines).ToList();
        incomeLines.Single(l => l.AccountCode == "90.1").Amount.Should().Be(300m);
        incomeLines.Single(l => l.AccountCode == "90.2").Amount.Should().Be(-50m, "contra revenue must reduce income");
        incomeLines.Single(l => l.AccountCode == "91").Amount.Should().Be(100m);

        var bs = await sp.GetRequiredService<IBalanceSheetReportReader>()
            .GetAsync(
                new BalanceSheetReportRequest
                {
                    AsOfPeriod = Period,
                    IncludeZeroAccounts = false,
                    IncludeNetIncomeInEquity = true
                },
                CancellationToken.None);

        bs.IsBalanced.Should().BeTrue();
        bs.Difference.Should().Be(0m);

        // Assets: Cash 850 + Equipment 400 + AccumDep -100 = 1150
        bs.TotalAssets.Should().Be(1150m);
        bs.TotalEquity.Should().Be(1150m);

        var assets = bs.Sections.Single(s => s.Section == StatementSection.Assets).Lines;
        assets.Single(l => l.AccountCode == "50").Amount.Should().Be(850m);
        assets.Single(l => l.AccountCode == "01").Amount.Should().Be(400m);
        assets.Single(l => l.AccountCode == "02").Amount.Should().Be(-100m, "contra assets must reduce total assets");

        var equity = bs.Sections.Single(s => s.Section == StatementSection.Equity).Lines;
        equity.Single(l => l.AccountCode == "80").Amount.Should().Be(1000m);
        equity.Single(l => l.AccountCode == "NET").Amount.Should().Be(150m);
    }

    private static async Task SeedCoAAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<IChartOfAccountsRepository>();
        var svc = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        async Task EnsureAsync(string code, string name, AccountType type, StatementSection section, bool isContra = false)
        {
            var existing = (await repo.GetForAdminAsync(includeDeleted: true))
                .FirstOrDefault(a => a.Account.Code == code && !a.IsDeleted);

            if (existing is not null)
            {
                if (!existing.IsActive)
                    await svc.SetActiveAsync(existing.Account.Id, true, CancellationToken.None);
                return;
            }

            await svc.CreateAsync(
                new CreateAccountRequest(
                    Code: code,
                    Name: name,
                    Type: type,
                    StatementSection: section,
                    IsContra: isContra,
                    NegativeBalancePolicy: NegativeBalancePolicy.Allow
                ),
                CancellationToken.None);
        }

        await EnsureAsync("50", "Cash", AccountType.Asset, StatementSection.Assets);
        await EnsureAsync("80", "Owner Equity", AccountType.Equity, StatementSection.Equity);

        await EnsureAsync("90.1", "Revenue", AccountType.Income, StatementSection.Income);
        await EnsureAsync("90.2", "Sales Returns", AccountType.Income, StatementSection.Income, isContra: true);

        await EnsureAsync("01", "Equipment", AccountType.Asset, StatementSection.Assets);
        await EnsureAsync("02", "Accumulated Depreciation", AccountType.Asset, StatementSection.Assets, isContra: true);

        await EnsureAsync("91", "Depreciation Expense", AccountType.Expense, StatementSection.Expenses);
    }

    private static async Task PostAsync(
        IHost host,
        Guid documentId,
        DateTime periodUtc,
        string debitCode,
        string creditCode,
        decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

        await posting.PostAsync(
            PostingOperation.Post,
            async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(
                    documentId,
                    periodUtc,
                    chart.Get(debitCode),
                    chart.Get(creditCode),
                    amount);
            },
            CancellationToken.None);
    }
}
