using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.Reports.AccountCard;
using NGB.Accounting.Reports.BalanceSheet;
using NGB.Accounting.Reports.GeneralJournal;
using NGB.Accounting.Reports.IncomeStatement;
using NGB.Persistence.Accounts;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

[Collection(PostgresCollection.Name)]
public sealed class ReportsGoldenTests(PostgresTestFixture fixture) : IntegrationTestBase(fixture)
{
    private static readonly DateTime PeriodUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Period = DateOnly.FromDateTime(PeriodUtc);

    [Fact]
    public async Task TrialBalance_Golden()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, revenueId, expensesId) = await SeedMinimalCoAAsync(host);

        var doc1 = Guid.CreateVersion7();
        var doc2 = Guid.CreateVersion7();

        await PostAsync(host, doc1, PeriodUtc, "50", "90.1", 100m);
        await PostAsync(host, doc2, PeriodUtc, "91", "50", 40m);

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<ITrialBalanceReader>();

        var rows = await reader.GetAsync(Period, Period, CancellationToken.None);

        rows.Should().HaveCount(3);

        var cash = rows.Single(r => r.AccountCode == "50");
        cash.AccountId.Should().Be(cashId);
        cash.OpeningBalance.Should().Be(0);
        cash.DebitAmount.Should().Be(100);
        cash.CreditAmount.Should().Be(40);
        cash.ClosingBalance.Should().Be(60);

        var revenue = rows.Single(r => r.AccountCode == "90.1");
        revenue.ClosingBalance.Should().Be(-100);

        var expenses = rows.Single(r => r.AccountCode == "91");
        expenses.ClosingBalance.Should().Be(40);
    }

    [Fact]
    public async Task GeneralJournal_Paging_Cursor_Keyset()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, _, _) = await SeedMinimalCoAAsync(host);

        var doc1 = Guid.CreateVersion7();
        var doc2 = Guid.CreateVersion7();

        await PostAsync(host, doc1, PeriodUtc, "50", "90.1", 100m);
        await PostAsync(host, doc2, PeriodUtc, "91", "50", 40m);

        await using var scope = host.Services.CreateAsyncScope();
        var journal = scope.ServiceProvider.GetRequiredService<IGeneralJournalReportReader>();

        var page1 = await journal.GetPageAsync(
            new GeneralJournalPageRequest
            {
                FromInclusive = Period,
                ToInclusive = Period,
                PageSize = 1
            },
            CancellationToken.None);

        page1.Lines.Should().HaveCount(1);
        page1.HasMore.Should().BeTrue();

        var page2 = await journal.GetPageAsync(
            new GeneralJournalPageRequest
            {
                FromInclusive = Period,
                ToInclusive = Period,
                PageSize = 1,
                Cursor = page1.NextCursor
            },
            CancellationToken.None);

        page2.Lines.Should().HaveCount(1);
        page2.HasMore.Should().BeFalse();

        var full = await journal.GetPageAsync(
            new GeneralJournalPageRequest
            {
                FromInclusive = Period,
                ToInclusive = Period,
                PageSize = 100
            },
            CancellationToken.None);

        full.Lines.Sum(x => x.Amount).Should().Be(140m);
    }

    [Fact]
    public async Task AccountCard_Paging_Cursor_Continuity()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, _, _) = await SeedMinimalCoAAsync(host);

        var doc1 = Guid.CreateVersion7();
        var doc2 = Guid.CreateVersion7();

        await PostAsync(host, doc1, PeriodUtc, "50", "90.1", 100m);
        await PostAsync(host, doc2, PeriodUtc, "91", "50", 40m);

        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var full = await ReportingTestHelpers.ReadAllAccountCardReportAsync(
            sp,
            cashId,
            Period,
            Period,
            ct: CancellationToken.None);

        var ordered = full.Lines
            .OrderBy(l => l.PeriodUtc)
            .ThenBy(l => l.EntryId)
            .ToArray();

        var paged = sp.GetRequiredService<IAccountCardEffectivePagedReportReader>();

        var page1 = await paged.GetPageAsync(
            new AccountCardReportPageRequest
            {
                AccountId = cashId,
                FromInclusive = Period,
                ToInclusive = Period,
                PageSize = 1
            },
            CancellationToken.None);

        page1.Lines.Should().HaveCount(1);
        page1.OpeningBalance.Should().Be(full.OpeningBalance);
        page1.Lines[0].EntryId.Should().Be(ordered[0].EntryId);
        page1.Lines[0].RunningBalance.Should().Be(ordered[0].RunningBalance);

        var page2 = await paged.GetPageAsync(
            new AccountCardReportPageRequest
            {
                AccountId = cashId,
                FromInclusive = Period,
                ToInclusive = Period,
                PageSize = 1,
                Cursor = page1.NextCursor
            },
            CancellationToken.None);

        page2.Lines.Should().HaveCount(1);

        page2.OpeningBalance.Should().Be(page1.NextCursor!.RunningBalance);
        page2.Lines[0].RunningBalance.Should().Be(ordered[1].RunningBalance);
        page2.Lines[0].RunningBalance
            .Should()
            .Be(page2.OpeningBalance + page2.Lines[0].Delta);

        page2.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task IncomeStatement_Golden()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, apId, equityId, revenueId, expensesId) = await SeedBalanceSheetCoAAsync(host);

        var doc1 = Guid.CreateVersion7(); // Owner investment
        var doc2 = Guid.CreateVersion7(); // Purchase on credit
        var doc3 = Guid.CreateVersion7(); // Pay part of AP
        var doc4 = Guid.CreateVersion7(); // Revenue in cash

        await PostAsync(host, doc1, PeriodUtc, "50", "80", 1000m);
        await PostAsync(host, doc2, PeriodUtc, "91", "60", 200m);
        await PostAsync(host, doc3, PeriodUtc, "60", "50", 50m);
        await PostAsync(host, doc4, PeriodUtc, "50", "90.1", 500m);

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

        report.FromInclusive.Should().Be(Period);
        report.ToInclusive.Should().Be(Period);

        report.TotalIncome.Should().Be(500m);
        report.TotalExpenses.Should().Be(200m);
        report.NetIncome.Should().Be(300m);
    }

    [Fact]
    public async Task BalanceSheet_Golden_WithNetIncome()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, apId, equityId, revenueId, expensesId) = await SeedBalanceSheetCoAAsync(host);

        var doc1 = Guid.CreateVersion7(); // Owner investment
        var doc2 = Guid.CreateVersion7(); // Purchase on credit
        var doc3 = Guid.CreateVersion7(); // Pay part of AP
        var doc4 = Guid.CreateVersion7(); // Revenue in cash

        await PostAsync(host, doc1, PeriodUtc, "50", "80", 1000m);
        await PostAsync(host, doc2, PeriodUtc, "91", "60", 200m);
        await PostAsync(host, doc3, PeriodUtc, "60", "50", 50m);
        await PostAsync(host, doc4, PeriodUtc, "50", "90.1", 500m);

        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var report = await sp.GetRequiredService<IBalanceSheetReportReader>()
            .GetAsync(
                new BalanceSheetReportRequest
                {
                    AsOfPeriod = Period,
                    IncludeZeroAccounts = false,
                    IncludeNetIncomeInEquity = true
                },
                CancellationToken.None);

        report.AsOfPeriod.Should().Be(Period);

        report.TotalAssets.Should().Be(1450m);
        report.TotalLiabilities.Should().Be(150m);
        report.TotalEquity.Should().Be(1300m);

        (report.TotalLiabilities + report.TotalEquity).Should().Be(report.TotalAssets);
    }

    [Fact]
    public async Task GeneralLedgerAggregated_Golden_Cash()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, _, _, _, _) = await SeedBalanceSheetCoAAsync(host);

        var doc1 = Guid.CreateVersion7(); // Owner investment
        var doc2 = Guid.CreateVersion7(); // Purchase on credit (no cash movement)
        var doc3 = Guid.CreateVersion7(); // Pay part of AP (cash out)
        var doc4 = Guid.CreateVersion7(); // Revenue in cash (cash in)

        await PostAsync(host, doc1, PeriodUtc, "50", "80", 1000m);
        await PostAsync(host, doc2, PeriodUtc, "91", "60", 200m);
        await PostAsync(host, doc3, PeriodUtc, "60", "50", 50m);
        await PostAsync(host, doc4, PeriodUtc, "50", "90.1", 500m);

        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var report = await ReportingTestHelpers.ReadAllGeneralLedgerAggregatedReportAsync(
            sp,
            cashId,
            Period,
            Period,
            ct: CancellationToken.None);

        report.AccountId.Should().Be(cashId);
        report.OpeningBalance.Should().Be(0m);
        report.TotalDebit.Should().Be(1500m);
        report.TotalCredit.Should().Be(50m);
        report.ClosingBalance.Should().Be(1450m);

        report.Lines.Should().HaveCount(3);
        report.Lines.Sum(l => l.DebitAmount).Should().Be(1500m);
        report.Lines.Sum(l => l.CreditAmount).Should().Be(50m);
        report.Lines.Last().RunningBalance.Should().Be(1450m);
    }

    private static async Task<(Guid cashId, Guid revenueId, Guid expensesId)> SeedMinimalCoAAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<IChartOfAccountsRepository>();
        var svc = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        async Task<Guid> GetOrCreateAsync(string code, string name, AccountType type)
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
                    IsContra: false,
                    NegativeBalancePolicy: NegativeBalancePolicy.Allow
                ),
                CancellationToken.None);
        }

        return (
            await GetOrCreateAsync("50", "Cash", AccountType.Asset),
            await GetOrCreateAsync("90.1", "Revenue", AccountType.Income),
            await GetOrCreateAsync("91", "Expenses", AccountType.Expense)
        );
    }

    private static async Task<(Guid cashId, Guid accountsPayableId, Guid equityId, Guid revenueId, Guid expensesId)> SeedBalanceSheetCoAAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<IChartOfAccountsRepository>();
        var svc = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        async Task<Guid> GetOrCreateAsync(string code, string name, AccountType type)
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
                    IsContra: false,
                    NegativeBalancePolicy: NegativeBalancePolicy.Allow
                ),
                CancellationToken.None);
        }

        return (
            await GetOrCreateAsync("50", "Cash", AccountType.Asset),
            await GetOrCreateAsync("60", "Accounts Payable", AccountType.Liability),
            await GetOrCreateAsync("80", "Owner's Equity", AccountType.Equity),
            await GetOrCreateAsync("90.1", "Revenue", AccountType.Income),
            await GetOrCreateAsync("91", "Expenses", AccountType.Expense)
        );
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
