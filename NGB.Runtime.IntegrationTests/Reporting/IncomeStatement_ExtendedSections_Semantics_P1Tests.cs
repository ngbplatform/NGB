using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.Reports.IncomeStatement;
using NGB.Persistence.Accounts;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

[Collection(PostgresCollection.Name)]
public sealed class IncomeStatement_ExtendedSections_Semantics_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateTime PeriodUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Period = DateOnly.FromDateTime(PeriodUtc);

    [Fact]
    public async Task IncomeStatement_IncludesCOGS_And_OtherIncomeExpense_WithCorrectTotals()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await SeedCoAAsync(host);

        // Owner funding so Cash never goes negative.
        await PostAsync(host, Guid.CreateVersion7(), PeriodUtc, "50", "80", 5000m);

        // Revenue: Cash +1000
        await PostAsync(host, Guid.CreateVersion7(), PeriodUtc, "50", "90.1", 1000m);
        // COGS: Cash -600
        await PostAsync(host, Guid.CreateVersion7(), PeriodUtc, "92", "50", 600m);
        // Expenses: Cash -100
        await PostAsync(host, Guid.CreateVersion7(), PeriodUtc, "91", "50", 100m);
        // Other income: Cash +50
        await PostAsync(host, Guid.CreateVersion7(), PeriodUtc, "50", "96", 50m);
        // Other expense: Cash -10
        await PostAsync(host, Guid.CreateVersion7(), PeriodUtc, "97", "50", 10m);

        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var report = await sp.GetRequiredService<IIncomeStatementReportReader>()
            .GetAsync(
                new IncomeStatementReportRequest
                {
                    FromInclusive = Period,
                    ToInclusive = Period,
                    IncludeZeroLines = false
                },
                CancellationToken.None);

        report.TotalIncome.Should().Be(1050m);
        report.TotalExpenses.Should().Be(710m);
        report.NetIncome.Should().Be(340m);
        report.TotalOtherIncome.Should().Be(50m);
        report.TotalOtherExpense.Should().Be(10m);

        var lines = report.Sections.SelectMany(s => s.Lines).ToList();
        lines.Single(l => l.AccountCode == "90.1").Amount.Should().Be(1000m);
        lines.Single(l => l.AccountCode == "92").Amount.Should().Be(600m);
        lines.Single(l => l.AccountCode == "91").Amount.Should().Be(100m);
        lines.Single(l => l.AccountCode == "96").Amount.Should().Be(50m);
        lines.Single(l => l.AccountCode == "97").Amount.Should().Be(10m);

        report.Sections.Select(s => s.Section).Should().Contain(new[]
        {
            StatementSection.Income,
            StatementSection.CostOfGoodsSold,
            StatementSection.Expenses,
            StatementSection.OtherIncome,
            StatementSection.OtherExpense
        });
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
        await EnsureAsync("92", "COGS", AccountType.Expense, StatementSection.CostOfGoodsSold);
        await EnsureAsync("91", "Operating Expenses", AccountType.Expense, StatementSection.Expenses);
        await EnsureAsync("96", "Other Income", AccountType.Income, StatementSection.OtherIncome);
        await EnsureAsync("97", "Other Expense", AccountType.Expense, StatementSection.OtherExpense);
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
