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
public sealed class InactiveAccounts_StillAppearInStatements_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateTime DayUtc = new(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Period = new(2026, 1, 1);

    [Fact]
    public async Task BalanceSheet_WhenAccountDeactivatedAfterMovements_StillShowsLine_AndRemainsBalanced()
    {
        // Arrange
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var ids = await SeedCoaAsync(host);

        // Owner investment: Cash +1000 / Equity +1000
        await PostAsync(host, Guid.CreateVersion7(), DayUtc, debitCode: "50", creditCode: "80", amount: 1000m);

        // Sale in cash: Cash +500 / Revenue +500
        await PostAsync(host, Guid.CreateVersion7(), DayUtc, debitCode: "50", creditCode: "90.1", amount: 500m);

        // Expense paid in cash: Expenses +200 / Cash -200
        await PostAsync(host, Guid.CreateVersion7(), DayUtc, debitCode: "91", creditCode: "50", amount: 200m);

        // Deactivate Cash after movements
        await SetActiveAsync(host, ids.cashId, isActive: false);

        // Act
        await using var scope = host.Services.CreateAsyncScope();
        var report = await scope.ServiceProvider.GetRequiredService<IBalanceSheetReportReader>()
            .GetAsync(
                new BalanceSheetReportRequest
                {
                    AsOfPeriod = Period,
                    IncludeZeroAccounts = false,
                    IncludeNetIncomeInEquity = true
                },
                CancellationToken.None);

        // Assert
        // Expected state:
        //   Cash: 1000 + 500 - 200 = 1300
        //   Equity: 1000
        //   Net income: 500 - 200 = 300
        //   Assets = Equity + Net = 1300

        report.IsBalanced.Should().BeTrue("statements must not drop inactive accounts with history");
        report.Difference.Should().Be(0m);
        report.TotalAssets.Should().Be(1300m);
        report.TotalEquity.Should().Be(1300m);

        var assets = report.Sections.Single(s => s.Section == StatementSection.Assets).Lines;
        assets.Should().ContainSingle(l => l.AccountCode == "50").Which.Amount.Should().Be(1300m);

        var equity = report.Sections.Single(s => s.Section == StatementSection.Equity).Lines;
        equity.Should().ContainSingle(l => l.AccountCode == "80").Which.Amount.Should().Be(1000m);
        equity.Should().ContainSingle(l => l.AccountCode == "NET").Which.Amount.Should().Be(300m);
    }

    [Fact]
    public async Task IncomeStatement_WhenAccountDeactivatedAfterMovements_StillShowsLine_AndNetIncomeIsCorrect()
    {
        // Arrange
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var ids = await SeedCoaAsync(host);

        // Sale in cash: Cash +500 / Revenue +500
        await PostAsync(host, Guid.CreateVersion7(), DayUtc, debitCode: "50", creditCode: "90.1", amount: 500m);

        // Expense paid in cash: Expenses +200 / Cash -200
        await PostAsync(host, Guid.CreateVersion7(), DayUtc, debitCode: "91", creditCode: "50", amount: 200m);

        // Deactivate Revenue after movements
        await SetActiveAsync(host, ids.revenueId, isActive: false);

        // Act
        await using var scope = host.Services.CreateAsyncScope();
        var report = await scope.ServiceProvider.GetRequiredService<IIncomeStatementReportReader>()
            .GetAsync(
                new IncomeStatementReportRequest
                {
                    FromInclusive = Period,
                    ToInclusive = Period,
                    IncludeZeroLines = false
                },
                CancellationToken.None);

        // Assert
        report.TotalIncome.Should().Be(500m, "income must include inactive accounts with historical activity");
        report.TotalExpenses.Should().Be(200m);
        report.NetIncome.Should().Be(300m);

        var lines = report.Sections.SelectMany(s => s.Lines).ToList();
        lines.Should().ContainSingle(l => l.AccountCode == "90.1").Which.Amount.Should().Be(500m);
        lines.Should().ContainSingle(l => l.AccountCode == "91").Which.Amount.Should().Be(200m);
    }

    private static async Task<(Guid cashId, Guid equityId, Guid revenueId, Guid expenseId)> SeedCoaAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IChartOfAccountsRepository>();
        var svc = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        async Task<Guid> EnsureAsync(string code, string name, AccountType type, StatementSection section)
        {
            var existing = (await repo.GetForAdminAsync(includeDeleted: true))
                .FirstOrDefault(a => a.Account.Code == code && !a.IsDeleted);

            if (existing is not null)
            {
                if (!existing.IsActive)
                    await svc.SetActiveAsync(existing.Account.Id, true, CancellationToken.None);
                return existing.Account.Id;
            }

            return await svc.CreateAsync(
                new CreateAccountRequest(
                    Code: code,
                    Name: name,
                    Type: type,
                    StatementSection: section,
                    NegativeBalancePolicy: NegativeBalancePolicy.Allow
                ),
                CancellationToken.None);
        }

        var cashId = await EnsureAsync("50", "Cash", AccountType.Asset, StatementSection.Assets);
        var equityId = await EnsureAsync("80", "Owner Equity", AccountType.Equity, StatementSection.Equity);
        var revenueId = await EnsureAsync("90.1", "Revenue", AccountType.Income, StatementSection.Income);
        var expenseId = await EnsureAsync("91", "Expenses", AccountType.Expense, StatementSection.Expenses);

        return (cashId, equityId, revenueId, expenseId);
    }

    private static async Task SetActiveAsync(IHost host, Guid accountId, bool isActive)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
        await svc.SetActiveAsync(accountId, isActive, CancellationToken.None);
    }

    private static async Task PostAsync(
        IHost host,
        Guid documentId,
        DateTime dateUtc,
        string debitCode,
        string creditCode,
        decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

        await posting.PostAsync(
            operation: PostingOperation.Post,
            postingAction: async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(documentId, dateUtc, chart.Get(debitCode), chart.Get(creditCode), amount);
            },
            ct: CancellationToken.None);
    }
}
