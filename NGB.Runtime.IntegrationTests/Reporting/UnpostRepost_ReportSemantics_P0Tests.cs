using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.Reports.BalanceSheet;
using NGB.Accounting.Reports.GeneralJournal;
using NGB.Accounting.Reports.IncomeStatement;
using NGB.Accounting.Reports.TrialBalance;
using NGB.Persistence.Accounts;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

[Collection(PostgresCollection.Name)]
public sealed class UnpostRepost_ReportSemantics_P0Tests(PostgresTestFixture fixture) : IntegrationTestBase(fixture)
{
    private static readonly DateTime PeriodUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Period = DateOnly.FromDateTime(PeriodUtc);

    [Fact]
    public async Task Post_Unpost_NetEffectIsZero_InAllCoreReports()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, _, _) = await SeedMinimalCoAAsync(host);

        var doc = Guid.CreateVersion7();

        // IMPORTANT:
        // PostingEngine idempotency is keyed by (document_id, operation). If we call Post twice with the same documentId,
        // the second call may be treated as a duplicate and skipped by posting_log.
        // Therefore, we post ALL entries of the document in a single Post operation.
        await PostEntriesAsync(host, doc, PeriodUtc,
            (debitCode: "50", creditCode: "90.1", amount: 100m),
            (debitCode: "91", creditCode: "50", amount: 40m)
        );

        // Sanity: posted numbers are not zero
        var baseline = await SnapshotCoreReportsAsync(host, cashId);

        baseline.TrialBalanceRows.Should().NotBeEmpty();
        baseline.CashClosing.Should().Be(60m);
        baseline.NetIncome.Should().Be(60m);

        // Act: Unpost => storno entries are written; net effect must become zero
        await UnpostAsync(host, doc);

        var afterUnpost = await SnapshotCoreReportsAsync(host, cashId);

        afterUnpost.CashClosing.Should().Be(0m, "Unpost writes storno so net effect for the document must become zero");
        afterUnpost.NetIncome.Should().Be(0m);

        afterUnpost.TrialBalanceRows.Sum(r => r.ClosingBalance).Should().Be(0m, "trial balance must net to zero after unpost");
        afterUnpost.BalanceSheetDifference.Should().Be(0m, "balance sheet must remain balanced");

        // Unpost does NOT delete original rows; it adds storno rows.
        afterUnpost.GeneralJournalLineCount.Should().BeGreaterThan(baseline.GeneralJournalLineCount);
    }

    [Fact]
    public async Task Repost_ReplacesOldAmounts_InAllCoreReports()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, _, _) = await SeedMinimalCoAAsync(host);

        var doc = Guid.CreateVersion7();

        await PostEntriesAsync(host, doc, PeriodUtc,
            (debitCode: "50", creditCode: "90.1", amount: 100m)
        );

        var baseline = await SnapshotCoreReportsAsync(host, cashId);
        baseline.CashClosing.Should().Be(100m);
        baseline.NetIncome.Should().Be(100m);

        // Act: Repost same document with a new amount (old is storno'd, new is posted)
        await RepostAsync(host, doc, newAmount: 250m);

        var afterRepost = await SnapshotCoreReportsAsync(host, cashId);
        afterRepost.CashClosing.Should().Be(250m);
        afterRepost.NetIncome.Should().Be(250m);

        // key invariant: report remains balanced
        afterRepost.BalanceSheetDifference.Should().Be(0m);
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

        var cashId = await GetOrCreateAsync("50", "Cash", AccountType.Asset);
        var revenueId = await GetOrCreateAsync("90.1", "Revenue", AccountType.Income);
        var expensesId = await GetOrCreateAsync("91", "Expenses", AccountType.Expense);

        return (cashId, revenueId, expensesId);
    }

    private static async Task PostEntriesAsync(
        IHost host,
        Guid documentId,
        DateTime periodUtc,
        params (string debitCode, string creditCode, decimal amount)[] lines)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

        await posting.PostAsync(
            operation: PostingOperation.Post,
            postingAction: async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);

                foreach (var (debitCode, creditCode, amount) in lines)
                {
                    ctx.Post(
                        documentId,
                        periodUtc,
                        chart.Get(debitCode),
                        chart.Get(creditCode),
                        amount);
                }
            },
            manageTransaction: true,
            ct: CancellationToken.None);
    }

    private static async Task UnpostAsync(IHost host, Guid documentId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<UnpostingService>();
        await svc.UnpostAsync(documentId, CancellationToken.None);
    }

    private static async Task RepostAsync(IHost host, Guid documentId, decimal newAmount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<RepostingService>();

        await svc.RepostAsync(
            documentId: documentId,
            async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(
                    documentId,
                    PeriodUtc,
                    chart.Get("50"),
                    chart.Get("90.1"),
                    newAmount);
            },
            ct: CancellationToken.None);
    }

    private static async Task<CoreSnapshot> SnapshotCoreReportsAsync(IHost host, Guid cashId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var tb = await sp.GetRequiredService<ITrialBalanceReader>()
            .GetAsync(Period, Period, CancellationToken.None);

        var balanceSheet = await sp.GetRequiredService<IBalanceSheetReportReader>()
            .GetAsync(new BalanceSheetReportRequest
            {
                AsOfPeriod = Period,
                IncludeZeroAccounts = false,
                IncludeNetIncomeInEquity = true
            }, CancellationToken.None);

        var income = await sp.GetRequiredService<IIncomeStatementReportReader>()
            .GetAsync(new IncomeStatementReportRequest
            {
                FromInclusive = Period,
                ToInclusive = Period,
                IncludeZeroLines = false
            }, CancellationToken.None);

        var ledger = await ReportingTestHelpers.ReadAllGeneralLedgerAggregatedReportAsync(
            sp,
            cashId,
            Period,
            Period,
            ct: CancellationToken.None);

        var gjPage = await sp.GetRequiredService<IGeneralJournalReader>()
            .GetPageAsync(new GeneralJournalPageRequest
            {
                FromInclusive = Period,
                ToInclusive = Period,
                PageSize = 500
            }, CancellationToken.None);

        var cashRow = tb.FirstOrDefault(r => r.AccountCode == "50");
        var cashClosing = cashRow?.ClosingBalance ?? 0m;

        var netIncome = ComputeNetIncome(income);

        return new CoreSnapshot(
            TrialBalanceRows: tb,
            CashClosing: cashClosing,
            NetIncome: netIncome,
            BalanceSheetDifference: balanceSheet.Difference,
            GeneralJournalLineCount: gjPage.Lines.Count,
            AccountCardClosing: ledger.ClosingBalance
        );
    }

    private static decimal ComputeNetIncome(IncomeStatementReport report)
    {
        decimal incomeTotal = 0m;
        decimal expenseTotal = 0m;

        foreach (var s in report.Sections)
        {
            if (s.Section == StatementSection.Income)
                incomeTotal += s.Total;

            if (s.Section == StatementSection.Expenses)
                expenseTotal += s.Total;
        }

        return incomeTotal - expenseTotal;
    }

    private sealed record CoreSnapshot(
        IReadOnlyList<TrialBalanceRow> TrialBalanceRows,
        decimal CashClosing,
        decimal NetIncome,
        decimal BalanceSheetDifference,
        int GeneralJournalLineCount,
        decimal AccountCardClosing
    );
}
