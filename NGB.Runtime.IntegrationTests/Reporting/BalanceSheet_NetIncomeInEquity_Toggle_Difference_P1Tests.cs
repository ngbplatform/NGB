using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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

[Collection(PostgresCollection.Name)]
public sealed class BalanceSheet_NetIncomeInEquity_Toggle_Difference_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateTime PeriodUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Period = DateOnly.FromDateTime(PeriodUtc);

    [Fact]
    public async Task BalanceSheet_IncludeNetIncomeInEquity_TogglesBalancingDifference()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await SeedCoAAsync(host);

        // Owner investment (equity) so balance sheet has a base.
        await PostAsync(host, Guid.CreateVersion7(), PeriodUtc, "50", "80", 1000m);

        // Revenue: +200
        await PostAsync(host, Guid.CreateVersion7(), PeriodUtc, "50", "90.1", 200m);
        // Expense: -50
        await PostAsync(host, Guid.CreateVersion7(), PeriodUtc, "91", "50", 50m);

        var expectedNetIncome = 150m;

        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var withNet = await sp.GetRequiredService<IBalanceSheetReportReader>()
            .GetAsync(
                new BalanceSheetReportRequest
                {
                    AsOfPeriod = Period,
                    IncludeZeroAccounts = false,
                    IncludeNetIncomeInEquity = true
                },
                CancellationToken.None);

        withNet.IsBalanced.Should().BeTrue();
        withNet.Difference.Should().Be(0m);

        var withoutNet = await sp.GetRequiredService<IBalanceSheetReportReader>()
            .GetAsync(
                new BalanceSheetReportRequest
                {
                    AsOfPeriod = Period,
                    IncludeZeroAccounts = false,
                    IncludeNetIncomeInEquity = false
                },
                CancellationToken.None);

        withoutNet.IsBalanced.Should().BeFalse();
        withoutNet.Difference.Should().Be(expectedNetIncome,
            "without synthetic Net Income line, Assets - (Liabilities+Equity) equals current net income");
    }

    private static async Task SeedCoAAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<IChartOfAccountsRepository>();
        var svc = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        async Task EnsureAsync(string code, string name, AccountType type, StatementSection section)
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
                    IsContra: false,
                    NegativeBalancePolicy: NegativeBalancePolicy.Allow
                ),
                CancellationToken.None);
        }

        await EnsureAsync("50", "Cash", AccountType.Asset, StatementSection.Assets);
        await EnsureAsync("80", "Owner Equity", AccountType.Equity, StatementSection.Equity);
        await EnsureAsync("90.1", "Revenue", AccountType.Income, StatementSection.Income);
        await EnsureAsync("91", "Expenses", AccountType.Expense, StatementSection.Expenses);
    }

    private static async Task PostAsync(IHost host, Guid doc, DateTime periodUtc, string debit, string credit, decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

        await posting.PostAsync(
            PostingOperation.Post,
            async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(doc, periodUtc, chart.Get(debit), chart.Get(credit), amount);
            },
            CancellationToken.None);
    }
}
