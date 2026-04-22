using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.PostingState;
using NGB.Accounting.Reports.GeneralLedgerAggregated;
using NGB.Accounting.Reports.TrialBalance;
using NGB.Core.Dimensions;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

/// <summary>
/// These tests lock down deterministic ordering for non-paged report endpoints.
/// Ordering is important for:
/// - stable UI rendering,
/// - deterministic golden outputs,
/// - reproducible diffs when comparing runs.
///
/// NOTE: Postgres orders UUIDs by their 16-byte value; the canonical UUID text
/// representation preserves that order, so we compare GUIDs via ToString("N").
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ReportOrdering_Deterministic_TrialBalance_GeneralLedgerAggregated_P2Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string CashCode = "50";
    private const string RevenueCode = "90.1";
    private const string ExpensesCode = "91";

    private static readonly Guid DocDay1_A = Guid.Parse("00000000-0000-0000-0000-000000000005");
    private static readonly Guid DocDay1_B = Guid.Parse("00000000-0000-0000-0000-000000000006");
    private static readonly Guid DocDay2_C = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private static readonly Guid ValueA = Guid.Parse("00000000-0000-0000-0000-0000000000AA");
    private static readonly Guid ValueB = Guid.Parse("00000000-0000-0000-0000-0000000000BB");

    [Fact]
    public async Task TrialBalanceRows_AreDeterministicallySorted_ByAccountCode_ThenDimensionSetId()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, _, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        await EnableDimensionsForCashAsync(host, cashId);
        await SeedLedgerDatasetAsync(host);

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<ITrialBalanceReader>();

        var rows = await reader.GetAsync(ReportingTestHelpers.Period, ReportingTestHelpers.Period, CancellationToken.None);

        rows.Should().NotBeEmpty();
        rows.Should().Contain(x => x.AccountCode == CashCode && x.DimensionSetId != Guid.Empty);

        // Lock down ordering: AccountCode (ordinal) then DimensionSetId (uuid order).
        AssertTrialBalanceSorted(rows);

        // Sanity: Cash should have at least 2 different dimension sets in this dataset.
        rows.Where(x => x.AccountCode == CashCode)
            .Select(x => x.DimensionSetId)
            .Distinct()
            .Count()
            .Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task GeneralLedgerAggregatedLines_AreDeterministicallySorted_ByPeriodUtc_DocumentId_CounterAccountCode_DimensionSetId()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, _, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        await EnableDimensionsForCashAsync(host, cashId);
        await SeedLedgerDatasetAsync(host);

        await using var scope = host.Services.CreateAsyncScope();

        var detailLines = await ReportingTestHelpers.ReadAllGeneralLedgerAggregatedLinesAsync(
            scope.ServiceProvider,
            cashId,
            ReportingTestHelpers.Period,
            ReportingTestHelpers.Period,
            dimensionScopes: null,
            ct: CancellationToken.None);

        detailLines.Should().NotBeEmpty();

        AssertLedgerLinesSorted(detailLines);

        // Also lock down that the application-level report preserves the same deterministic ordering.
        var reportResult = await ReportingTestHelpers.ReadAllGeneralLedgerAggregatedReportAsync(
            scope.ServiceProvider,
            cashId,
            ReportingTestHelpers.Period,
            ReportingTestHelpers.Period,
            dimensionScopes: null,
            ct: CancellationToken.None);

        reportResult.Lines.Should().NotBeEmpty();
        AssertLedgerLinesSorted(reportResult.Lines);

        // Additional, very targeted assertions so that if ordering changes we get a very actionable diff:
        // - all Day1 rows must come before any Day2 rows
        // - within DocDay1_B, CounterAccountCode must be ordered (Revenue before Expenses)
        var day1Count = detailLines.Count(x => x.PeriodUtc == ReportingTestHelpers.Day1Utc);
        day1Count.Should().BeGreaterThan(0);
        detailLines.Take(day1Count).All(x => x.PeriodUtc == ReportingTestHelpers.Day1Utc).Should().BeTrue();

        var docB = detailLines.Where(x => x.DocumentId == DocDay1_B).ToList();
        docB.Should().NotBeEmpty();

        var counterCodes = docB.Select(x => x.CounterAccountCode).ToArray();
        counterCodes.Should().BeInAscendingOrder(StringComparer.Ordinal);

        // Within the Revenue counter-account inside DocDay1_B, DimensionSetId must be ordered.
        var docBRevenue = docB.Where(x => x.CounterAccountCode == RevenueCode).ToList();
        docBRevenue.Count.Should().BeGreaterThanOrEqualTo(2);
        AssertGuidsSortedByUuidOrder(docBRevenue.Select(x => x.DimensionSetId).ToArray());
    }

    private static async Task EnableDimensionsForCashAsync(IHost host, Guid cashId)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var coa = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        // Add a single optional dimension rule (enables DimensionBag usage for Cash).
        await coa.UpdateAsync(
            new UpdateAccountRequest(
                AccountId: cashId,
                DimensionRules: [new AccountDimensionRuleRequest("dept", IsRequired: false)]),
            CancellationToken.None);
    }

    private static async Task SeedLedgerDatasetAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

        // A: Day1, single line (Cash[A] -> Revenue)
        await PostCashDebitAsync(posting, DocDay1_A, ReportingTestHelpers.Day1Utc,
            creditAccountCode: RevenueCode,
            amount: 10m,
            cashValueId: ValueA);

        // B: Day1, multi-line document (Cash[A] -> Revenue, Cash[B] -> Revenue, Cash[A] -> Expenses)
        await posting.PostAsync(
            PostingOperation.Post,
            async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                var cash = chart.Get(CashCode);
                var dimId = cash.DimensionRules.Single().DimensionId;

                var revenue = chart.Get(RevenueCode);
                var expenses = chart.Get(ExpensesCode);

                ctx.Post(
                    DocDay1_B,
                    ReportingTestHelpers.Day1Utc,
                    cash,
                    revenue,
                    20m,
                    debitDimensions: new DimensionBag([new DimensionValue(dimId, ValueA)]),
                    creditDimensions: DimensionBag.Empty);

                ctx.Post(
                    DocDay1_B,
                    ReportingTestHelpers.Day1Utc,
                    cash,
                    revenue,
                    30m,
                    debitDimensions: new DimensionBag([new DimensionValue(dimId, ValueB)]),
                    creditDimensions: DimensionBag.Empty);

                ctx.Post(
                    DocDay1_B,
                    ReportingTestHelpers.Day1Utc,
                    cash,
                    expenses,
                    5m,
                    debitDimensions: new DimensionBag([new DimensionValue(dimId, ValueA)]),
                    creditDimensions: DimensionBag.Empty);
            },
            CancellationToken.None);

        // C: Day2, single line (Cash[A] -> Revenue), with a SMALLER doc id than Day1 docs.
        // This locks down the fact that PeriodUtc is the primary ordering key.
        await PostCashDebitAsync(posting, DocDay2_C, ReportingTestHelpers.Day2Utc,
            creditAccountCode: RevenueCode,
            amount: 1m,
            cashValueId: ValueA);
    }

    private static async Task PostCashDebitAsync(
        PostingEngine posting,
        Guid documentId,
        DateTime periodUtc,
        string creditAccountCode,
        decimal amount,
        Guid cashValueId)
    {
        await posting.PostAsync(
            PostingOperation.Post,
            async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                var cash = chart.Get(CashCode);
                var dimId = cash.DimensionRules.Single().DimensionId;

                var credit = chart.Get(creditAccountCode);

                ctx.Post(
                    documentId,
                    periodUtc,
                    cash,
                    credit,
                    amount,
                    debitDimensions: new DimensionBag([new DimensionValue(dimId, cashValueId)]),
                    creditDimensions: DimensionBag.Empty);
            },
            CancellationToken.None);
    }

    private static void AssertTrialBalanceSorted(IReadOnlyList<TrialBalanceRow> rows)
    {
        for (var i = 1; i < rows.Count; i++)
        {
            var prev = rows[i - 1];
            var cur = rows[i];

            var codeCmp = StringComparer.Ordinal.Compare(prev.AccountCode, cur.AccountCode);
            if (codeCmp < 0)
                continue;

            if (codeCmp > 0)
            {
                throw new Xunit.Sdk.XunitException(
                    $"TrialBalance order violation at index {i}: '{prev.AccountCode}' must be <= '{cur.AccountCode}'.");
            }

            // Same account code -> DimensionSetId must be non-decreasing.
            CompareUuidAsText(prev.DimensionSetId, cur.DimensionSetId)
                .Should().BeLessThanOrEqualTo(0,
                    $"TrialBalance.DimensionSetId must be ordered for AccountCode='{cur.AccountCode}' (index {i})");
        }
    }

    private static void AssertLedgerLinesSorted(IReadOnlyList<GeneralLedgerAggregatedLine> lines)
    {
        for (var i = 1; i < lines.Count; i++)
        {
            var prev = lines[i - 1];
            var cur = lines[i];

            if (prev.PeriodUtc < cur.PeriodUtc)
                continue;

            if (prev.PeriodUtc > cur.PeriodUtc)
            {
                throw new Xunit.Sdk.XunitException(
                    $"GeneralLedgerAggregated order violation at index {i}: PeriodUtc must be non-decreasing.");
            }

            var docCmp = CompareUuidAsText(prev.DocumentId, cur.DocumentId);
            if (docCmp < 0)
                continue;

            if (docCmp > 0)
            {
                throw new Xunit.Sdk.XunitException(
                    $"GeneralLedgerAggregated order violation at index {i}: DocumentId must be ordered (PeriodUtc tie)." +
                    $" Prev={prev.DocumentId}, Cur={cur.DocumentId}");
            }

            var counterCmp = StringComparer.Ordinal.Compare(prev.CounterAccountCode, cur.CounterAccountCode);
            if (counterCmp < 0)
                continue;

            if (counterCmp > 0)
            {
                throw new Xunit.Sdk.XunitException(
                    $"GeneralLedgerAggregated order violation at index {i}: CounterAccountCode must be ordered (PeriodUtc+DocumentId tie)." +
                    $" Prev='{prev.CounterAccountCode}', Cur='{cur.CounterAccountCode}'");
            }

            CompareUuidAsText(prev.DimensionSetId, cur.DimensionSetId)
                .Should().BeLessThanOrEqualTo(0,
                    $"GeneralLedgerAggregated.DimensionSetId must be ordered (PeriodUtc+DocumentId+CounterAccountCode tie) at index {i}");
        }
    }

    private static void AssertLedgerLinesSorted(IReadOnlyList<GeneralLedgerAggregatedReportLine> lines)
    {
        for (var i = 1; i < lines.Count; i++)
        {
            var prev = lines[i - 1];
            var cur = lines[i];

            if (prev.PeriodUtc < cur.PeriodUtc)
                continue;

            if (prev.PeriodUtc > cur.PeriodUtc)
            {
                throw new Xunit.Sdk.XunitException(
                    $"GeneralLedgerAggregatedReport order violation at index {i}: PeriodUtc must be non-decreasing.");
            }

            var docCmp = CompareUuidAsText(prev.DocumentId, cur.DocumentId);
            if (docCmp < 0)
                continue;

            if (docCmp > 0)
            {
                throw new Xunit.Sdk.XunitException(
                    $"GeneralLedgerAggregatedReport order violation at index {i}: DocumentId must be ordered (PeriodUtc tie)." +
                    $" Prev={prev.DocumentId}, Cur={cur.DocumentId}");
            }

            var counterCmp = StringComparer.Ordinal.Compare(prev.CounterAccountCode, cur.CounterAccountCode);
            if (counterCmp < 0)
                continue;

            if (counterCmp > 0)
            {
                throw new Xunit.Sdk.XunitException(
                    $"GeneralLedgerAggregatedReport order violation at index {i}: CounterAccountCode must be ordered (PeriodUtc+DocumentId tie)." +
                    $" Prev='{prev.CounterAccountCode}', Cur='{cur.CounterAccountCode}'");
            }

            CompareUuidAsText(prev.DimensionSetId, cur.DimensionSetId)
                .Should().BeLessThanOrEqualTo(0,
                    $"GeneralLedgerAggregatedReport.DimensionSetId must be ordered (PeriodUtc+DocumentId+CounterAccountCode tie) at index {i}");
        }
    }

    private static void AssertGuidsSortedByUuidOrder(Guid[] ids)
    {
        for (var i = 1; i < ids.Length; i++)
        {
            CompareUuidAsText(ids[i - 1], ids[i])
                .Should().BeLessThanOrEqualTo(0, $"UUIDs must be ordered at index {i}");
        }
    }

    private static int CompareUuidAsText(Guid a, Guid b)
        => StringComparer.Ordinal.Compare(a.ToString("N"), b.ToString("N"));
}
