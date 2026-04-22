using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.Reports.AccountCard;
using NGB.Accounting.Reports.BalanceSheet;
using NGB.Accounting.Reports.IncomeStatement;
using NGB.Persistence.Accounts;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Periods;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

[Collection(PostgresCollection.Name)]
public sealed class ReportingGoldenTests(PostgresTestFixture fixture) : IntegrationTestBase(fixture)
{
    private static readonly DateTime PeriodUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Period = DateOnly.FromDateTime(PeriodUtc);

    private static readonly DateTime PrevPeriodUtc = new(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly PrevPeriod = DateOnly.FromDateTime(PrevPeriodUtc);

    [Fact]
    public async Task IncomeStatement_Golden_Basic()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        _ = await SeedReportingCoAAsync(host);

        await SeedTypicalBusinessScenarioAsync(host);

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IIncomeStatementReportReader>();

        var report = await reader.GetAsync(
            new IncomeStatementReportRequest
            {
                FromInclusive = Period,
                ToInclusive = Period
            },
            CancellationToken.None);

        report.TotalIncome.Should().Be(500m);
        report.TotalExpenses.Should().Be(200m);
        report.NetIncome.Should().Be(300m);

        // smoke: sections must exist and be consistent with totals
        report.Sections.Should().NotBeEmpty();
        // Report line amounts are typically expressed as positive values within each section
        // (income lines positive; expense lines positive). NetIncome is computed as income - expenses.
        var linesSum = report.Sections.SelectMany(s => s.Lines).Sum(l => l.Amount);
        linesSum.Should().Be(report.TotalIncome + report.TotalExpenses);
    }

    [Fact]
    public async Task BalanceSheet_Golden_IncludeNetIncomeInEquity()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        _ = await SeedReportingCoAAsync(host);

        await SeedTypicalBusinessScenarioAsync(host);

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IBalanceSheetReportReader>();

        // With IncludeNetIncomeInEquity=true, BS must balance even if P&L accounts are not closed.
        var report = await reader.GetAsync(
            new BalanceSheetReportRequest
            {
                AsOfPeriod = Period,
                IncludeNetIncomeInEquity = true
            },
            CancellationToken.None);

        // Expected balances for the scenario:
        // Cash: +1000 - 50 + 500 = 1450
        // AP: +200 - 50 = 150
        // Equity: +1000
        // NetIncome: +300
        report.TotalAssets.Should().Be(1450m);
        report.TotalLiabilities.Should().Be(150m);
        report.TotalEquity.Should().Be(1300m);

        report.IsBalanced.Should().BeTrue();
        report.Difference.Should().Be(0m);
        report.TotalLiabilitiesAndEquity.Should().Be(report.TotalAssets);
    }

    [Fact]
    public async Task GeneralLedgerAggregated_Golden_Cash()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (cashId, _, _, _, _) = await SeedReportingCoAAsync(host);

        await SeedTypicalBusinessScenarioAsync(host);

        await using var scope = host.Services.CreateAsyncScope();
        var report = await ReportingTestHelpers.ReadAllGeneralLedgerAggregatedReportAsync(
            scope.ServiceProvider,
            cashId,
            Period,
            Period,
            ct: CancellationToken.None);

        report.OpeningBalance.Should().Be(0m);
        report.TotalDebit.Should().Be(1500m);
        report.TotalCredit.Should().Be(50m);
        report.ClosingBalance.Should().Be(1450m);

        // Cash movements in this scenario: equity injection, revenue, payment to AP.
        report.Lines.Should().HaveCount(3);
        report.Lines.Last().RunningBalance.Should().Be(1450m);
    }

    [Fact]
    public async Task AccountCard_OpeningBalance_BeforeRange_KeysetPaging_Golden()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (cashId, _, _, _, _) = await SeedReportingCoAAsync(host);

        // Prev period movements (should become opening balance for Period)
        var d1 = Guid.CreateVersion7();
        var d2 = Guid.CreateVersion7();
        await PostAsync(host, d1, PrevPeriodUtc, "50", "80", 1000m); // Cash +1000
        await PostAsync(host, d2, PrevPeriodUtc, "91", "50", 200m);  // Cash -200 => opening = 800

        // Opening balance for Account Card is derived from the latest CLOSED balances snapshot.
        // Therefore we must close the previous month to materialize balances.
        await CloseMonthAsync(host, PrevPeriod);

        // Period movements (should be returned as lines)
        var d3 = Guid.CreateVersion7();
        var d4 = Guid.CreateVersion7();
        await PostAsync(host, d3, PeriodUtc, "50", "90.1", 500m); // Cash +500
        await PostAsync(host, d4, PeriodUtc, "60", "50", 50m);    // Cash -50

        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var full = await ReportingTestHelpers.ReadAllAccountCardReportAsync(
            sp,
            cashId,
            Period,
            Period,
            ct: CancellationToken.None);

        full.OpeningBalance.Should().Be(800m);
        full.Lines.Should().HaveCount(2);
        full.Lines[0].RunningBalance.Should().Be(1300m);
        full.Lines[1].RunningBalance.Should().Be(1250m);

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

        page1.OpeningBalance.Should().Be(800m);
        page1.Lines.Should().HaveCount(1);
        page1.Lines[0].RunningBalance.Should().Be(1300m);
        page1.HasMore.Should().BeTrue();
        page1.NextCursor.Should().NotBeNull();

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

        page2.OpeningBalance.Should().Be(page1.NextCursor!.RunningBalance);
        page2.Lines.Should().HaveCount(1);
        page2.Lines[0].RunningBalance.Should().Be(1250m);
        page2.HasMore.Should().BeFalse();
        page2.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task AccountCard_KeysetPaging_NoGaps_NoDuplicates_ThreePages()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (cashId, _, _, _, _) = await SeedReportingCoAAsync(host);

        // 3 cash lines in the same month; we assert cursor paging has no gaps/duplicates.
        var d1 = Guid.CreateVersion7();
        var d2 = Guid.CreateVersion7();
        var d3 = Guid.CreateVersion7();

        await PostAsync(host, d1, PeriodUtc, "50", "80", 1000m);  // +1000
        await PostAsync(host, d2, PeriodUtc, "50", "90.1", 500m); // +500
        await PostAsync(host, d3, PeriodUtc, "60", "50", 50m);    // -50

        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var full = await ReportingTestHelpers.ReadAllAccountCardReportAsync(
            sp,
            cashId,
            Period,
            Period,
            ct: CancellationToken.None);

        full.OpeningBalance.Should().Be(0m);
        full.TotalDebit.Should().Be(1500m);
        full.TotalCredit.Should().Be(50m);
        full.ClosingBalance.Should().Be(1450m);

        full.Lines.Should().HaveCount(3);
        full.Lines.Sum(l => l.DebitAmount).Should().Be(full.TotalDebit);
        full.Lines.Sum(l => l.CreditAmount).Should().Be(full.TotalCredit);

        // Canonical ordering (must match keyset order): (PeriodUtc, EntryId)
        var expectedOrdered = full.Lines
            .OrderBy(l => l.PeriodUtc)
            .ThenBy(l => l.EntryId)
            .ToArray();

        var expectedIds = expectedOrdered.Select(l => l.EntryId).ToArray();
        var expectedById = expectedOrdered.ToDictionary(l => l.EntryId);

        var paged = sp.GetRequiredService<IAccountCardEffectivePagedReportReader>();

        var p1 = await paged.GetPageAsync(new AccountCardReportPageRequest
        {
            AccountId = cashId,
            FromInclusive = Period,
            ToInclusive = Period,
            PageSize = 1
        }, CancellationToken.None);

        var p2 = await paged.GetPageAsync(new AccountCardReportPageRequest
        {
            AccountId = cashId,
            FromInclusive = Period,
            ToInclusive = Period,
            PageSize = 1,
            Cursor = p1.NextCursor
        }, CancellationToken.None);

        var p3 = await paged.GetPageAsync(new AccountCardReportPageRequest
        {
            AccountId = cashId,
            FromInclusive = Period,
            ToInclusive = Period,
            PageSize = 1,
            Cursor = p2.NextCursor
        }, CancellationToken.None);

        // Totals must be independent of cursor paging and stable across pages.
        foreach (var p in new[] { p1, p2, p3 })
        {
            p.TotalDebit.Should().Be(full.TotalDebit);
            p.TotalCredit.Should().Be(full.TotalCredit);
            p.ClosingBalance.Should().Be(full.ClosingBalance);
            p.Lines.Should().HaveCount(1);

            var line = p.Lines.Single();
            var expected = expectedById[line.EntryId];

            line.DebitAmount.Should().Be(expected.DebitAmount);
            line.CreditAmount.Should().Be(expected.CreditAmount);
            line.CounterAccountCode.Should().Be(expected.CounterAccountCode);
            line.RunningBalance.Should().Be(expected.RunningBalance);
        }

        // Cursor-driven opening balance currency.
        p1.OpeningBalance.Should().Be(full.OpeningBalance);
        p2.OpeningBalance.Should().Be(p1.Lines.Single().RunningBalance);
        p3.OpeningBalance.Should().Be(p2.Lines.Single().RunningBalance);

        // no duplicates, no gaps, and the sequence matches the canonical ordering
        var ids = new[]
        {
            p1.Lines.Single().EntryId,
            p2.Lines.Single().EntryId,
            p3.Lines.Single().EntryId
        };

        ids.Should().Equal(expectedIds);
        ids.Distinct().Should().HaveCount(3);

        // last page must finish the cursor sequence
        p3.HasMore.Should().BeFalse();
        p3.NextCursor.Should().BeNull();
    }

    private static async Task SeedTypicalBusinessScenarioAsync(IHost host)
    {
        // 1) Owner equity injection: Dr Cash / Cr Equity
        await PostAsync(host, Guid.CreateVersion7(), PeriodUtc, "50", "80", 1000m);

        // 2) Buy on credit: Dr Expenses / Cr AP
        await PostAsync(host, Guid.CreateVersion7(), PeriodUtc, "91", "60", 200m);

        // 3) Partial pay AP: Dr AP / Cr Cash
        await PostAsync(host, Guid.CreateVersion7(), PeriodUtc, "60", "50", 50m);

        // 4) Cash revenue: Dr Cash / Cr Revenue
        await PostAsync(host, Guid.CreateVersion7(), PeriodUtc, "50", "90.1", 500m);
    }

    private static async Task<(Guid cashId, Guid apId, Guid equityId, Guid revenueId, Guid expensesId)> SeedReportingCoAAsync(IHost host)
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

    private static async Task CloseMonthAsync(IHost host, DateOnly period)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var closing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();
        await closing.CloseMonthAsync(period, closedBy: "test", ct: CancellationToken.None);
    }
}
