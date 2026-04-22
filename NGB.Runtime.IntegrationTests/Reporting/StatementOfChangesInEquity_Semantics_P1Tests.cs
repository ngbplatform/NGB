using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.Reports.StatementOfChangesInEquity;
using NGB.Persistence.Accounts;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Periods;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

[Collection(PostgresCollection.Name)]
public sealed class StatementOfChangesInEquity_Semantics_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task OpenYear_Report_Shows_Real_Equity_And_CurrentEarnings()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var jan = new DateOnly(2033, 1, 1);
        var janUtc = Utc(jan);

        await SeedReportingCoAAsync(sp);

        await PostAsync(sp, Guid.CreateVersion7(), janUtc, debit: "50", credit: "80", amount: 1000m);
        await PostAsync(sp, Guid.CreateVersion7(), janUtc, debit: "50", credit: "90.1", amount: 500m);
        await PostAsync(sp, Guid.CreateVersion7(), janUtc, debit: "91", credit: "50", amount: 200m);

        var reader = sp.GetRequiredService<IStatementOfChangesInEquityReportReader>();

        var report = await reader.GetAsync(
            new StatementOfChangesInEquityReportRequest
            {
                FromInclusive = jan,
                ToInclusive = jan
            },
            CancellationToken.None);

        report.Lines.Select(x => x.ComponentCode).Should().Equal("80", "CURR_EARNINGS");
        report.Lines.Single(x => x.ComponentCode == "80").ClosingAmount.Should().Be(1000m);
        report.Lines.Single(x => x.ComponentCode == "CURR_EARNINGS").ClosingAmount.Should().Be(300m);
        report.TotalOpening.Should().Be(0m);
        report.TotalChange.Should().Be(1300m);
        report.TotalClosing.Should().Be(1300m);
    }

    [Fact]
    public async Task AfterCloseFiscalYear_Report_Moves_Profit_Into_RetainedEarnings_And_Drops_CurrentEarnings()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var jan = new DateOnly(2034, 1, 1);
        var janUtc = Utc(jan);

        await SeedReportingCoAAsync(sp);

        await PostAsync(sp, Guid.CreateVersion7(), janUtc, debit: "50", credit: "80", amount: 1000m);
        await PostAsync(sp, Guid.CreateVersion7(), janUtc, debit: "91", credit: "60", amount: 200m);
        await PostAsync(sp, Guid.CreateVersion7(), janUtc, debit: "60", credit: "50", amount: 50m);
        await PostAsync(sp, Guid.CreateVersion7(), janUtc, debit: "50", credit: "90.1", amount: 500m);

        var closing = sp.GetRequiredService<IPeriodClosingService>();
        for (var m = 1; m <= 11; m++)
            await closing.CloseMonthAsync(new DateOnly(2034, m, 1), closedBy: "tests");

        var retainedEarningsId = await GetAccountIdByCodeAsync(sp, "84");
        await closing.CloseFiscalYearAsync(new DateOnly(2034, 12, 1), retainedEarningsId, closedBy: "tests");

        var reader = sp.GetRequiredService<IStatementOfChangesInEquityReportReader>();
        var report = await reader.GetAsync(
            new StatementOfChangesInEquityReportRequest
            {
                FromInclusive = jan,
                ToInclusive = new DateOnly(2034, 12, 1)
            },
            CancellationToken.None);

        report.Lines.Select(x => x.ComponentCode).Should().Equal("80", "84");
        report.Lines.Single(x => x.ComponentCode == "80").ClosingAmount.Should().Be(1000m);
        report.Lines.Single(x => x.ComponentCode == "84").ClosingAmount.Should().Be(300m);
        report.Lines.Should().NotContain(x => x.ComponentCode == "CURR_EARNINGS");
        report.TotalOpening.Should().Be(0m);
        report.TotalChange.Should().Be(1300m);
        report.TotalClosing.Should().Be(1300m);
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
