using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.Reports.AccountCard;
using NGB.Accounting.Reports.GeneralJournal;
using NGB.Core.Dimensions;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Periods;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

[Collection(PostgresCollection.Name)]
public sealed class Reports_DimensionSets_NotCollapsed_ByDimensionSetId_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task GeneralLedgerAggregated_WhenTwoDimensionSetsSharePrefixValues_DoesNotCollapseRows()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, revenueId, bagA, bagB, s1, s2, s3, dim4, v4a, v4b) = await SeedCashWithFourDimsAsync(host);

        var docId = Guid.CreateVersion7();
        var dt = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);

        await PostTwoCashDebitsAsync(host, docId, dt, bagA, bagB);

        await using var scope = host.Services.CreateAsyncScope();

        var d1 = bagA.Items.Single(v => v.ValueId == s1).DimensionId;
        var d2 = bagA.Items.Single(v => v.ValueId == s2).DimensionId;
        var d3 = bagA.Items.Single(v => v.ValueId == s3).DimensionId;

        var dimsFilter = new DimensionScopeBag([
            new DimensionScope(d1, [s1]),
            new DimensionScope(d2, [s2]),
            new DimensionScope(d3, [s3])
        ]);

        var rows = await ReportingTestHelpers.ReadAllGeneralLedgerAggregatedLinesAsync(
            scope.ServiceProvider,
            cashId,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 1, 1),
            dimensionScopes: dimsFilter,
            ct: CancellationToken.None);

        // If grouping key was (DocumentId, CounterAccount, first 3 dimensions), these two lines would collapse into one.
        var target = rows.Where(r => r.DocumentId == docId && r.CounterAccountId == revenueId).ToList();
        target.Should().HaveCount(2);

        target.Select(x => x.DimensionSetId).Distinct().Should().HaveCount(2);
        target.Select(x => x.DimensionSetId).Should().NotContain(Guid.Empty);

        target.Sum(x => x.DebitAmount).Should().Be(300m);
        target.Sum(x => x.CreditAmount).Should().Be(0m);

        // Both rows share first-3-dimensions projection, but differ by 4th dimension.
        target.All(x => dimsFilter.All(scope => scope.ValueIds.All(valueId => x.Dimensions.Items.Contains(new DimensionValue(scope.DimensionId, valueId))))).Should().BeTrue();

        target.Select(x => x.Dimensions.Single(v => v.DimensionId == dim4).ValueId)
            .Should().BeEquivalentTo([v4a, v4b]);
    }

    [Fact]
    public async Task AccountCardPagedReport_OpeningBalance_SumsAcrossDimensionSetsSharingPrefixValues()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, _, bagA, bagB, s1, s2, s3, _, _, _) = await SeedCashWithFourDimsAsync(host);

        var d1 = bagA.Items.Single(v => v.ValueId == s1).DimensionId;
        var d2 = bagA.Items.Single(v => v.ValueId == s2).DimensionId;
        var d3 = bagA.Items.Single(v => v.ValueId == s3).DimensionId;

        var dimsFilter = new DimensionScopeBag([
            new DimensionScope(d1, [s1]),
            new DimensionScope(d2, [s2]),
            new DimensionScope(d3, [s3])
        ]);

        // Two dimension sets in January, same first-3-dimensions projection but different 4th dimension.
        await PostTwoCashDebitsAsync(host, Guid.CreateVersion7(), new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc), bagA, bagB);

        // Close January so balances snapshot exists.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var closing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();
            await closing.CloseMonthAsync(new DateOnly(2026, 1, 1), closedBy: "test", CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAccountCardEffectivePagedReportReader>();

            // February report with dimension filters: opening should sum both January closing balances (100 + 200).
            var page = await reader.GetPageAsync(new AccountCardReportPageRequest
            {
                AccountId = cashId,
                FromInclusive = new DateOnly(2026, 2, 1),
                ToInclusive = new DateOnly(2026, 2, 1),
                DimensionScopes = dimsFilter,
                PageSize = 50
            }, CancellationToken.None);

            page.Lines.Should().BeEmpty("no movements in February");
            page.OpeningBalance.Should().Be(300m);
            page.TotalDebit.Should().Be(0m);
            page.TotalCredit.Should().Be(0m);
            page.ClosingBalance.Should().Be(300m);
        }
    }

    [Fact]
    public async Task GeneralJournalReader_Populates_DimensionSetIds_And_Dimensions_ForBothSides()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (_, _, bagA, bagB, _, _, _, dim4, v4a, v4b) = await SeedCashWithFourDimsAsync(host);

        var docId = Guid.CreateVersion7();
        var dt = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);
        await PostTwoCashDebitsAsync(host, docId, dt, bagA, bagB);

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IGeneralJournalReader>();

        var page = await reader.GetPageAsync(new GeneralJournalPageRequest
        {
            FromInclusive = new DateOnly(2026, 1, 1),
            ToInclusive = new DateOnly(2026, 1, 1),
            DocumentId = docId,
            PageSize = 50
        }, CancellationToken.None);

        page.HasMore.Should().BeFalse();
        page.Lines.Should().HaveCount(2);

        page.Lines.Select(l => l.DebitDimensionSetId).Distinct().Should().HaveCount(2);
        page.Lines.Select(l => l.DebitDimensionSetId).Should().NotContain(Guid.Empty);

        // Credit side has no dimensions.
        page.Lines.All(l => l.CreditDimensionSetId == Guid.Empty).Should().BeTrue();
        page.Lines.All(l => l.CreditDimensions.IsEmpty).Should().BeTrue();

        // Each line should preserve the 4th dimension values.
        page.Lines.Select(l => l.DebitDimensions.Single(v => v.DimensionId == dim4).ValueId)
            .Should().BeEquivalentTo([v4a, v4b]);
    }

    private static async Task<(Guid cashId, Guid revenueId, DimensionBag bagA, DimensionBag bagB, Guid s1, Guid s2, Guid s3, Guid dim4, Guid v4a, Guid v4b)>
        SeedCashWithFourDimsAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var coa = sp.GetRequiredService<IChartOfAccountsManagementService>();

        await coa.CreateAsync(new CreateAccountRequest(
            Code: "1010",
            Name: "Cash",
            Type: AccountType.Asset,
            IsContra: false,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow,
            DimensionRules:
            [
                new AccountDimensionRuleRequest(DimensionCode: "dim1", IsRequired: true, Ordinal: 10),
                new AccountDimensionRuleRequest(DimensionCode: "dim2", IsRequired: true, Ordinal: 20),
                new AccountDimensionRuleRequest(DimensionCode: "dim3", IsRequired: true, Ordinal: 30),
                new AccountDimensionRuleRequest(DimensionCode: "dim4", IsRequired: true, Ordinal: 40)
            ]),
            CancellationToken.None);

        await coa.CreateAsync(new CreateAccountRequest(
            Code: "9010",
            Name: "Revenue",
            Type: AccountType.Income),
            CancellationToken.None);

        var chart = await sp.GetRequiredService<IChartOfAccountsProvider>().GetAsync(CancellationToken.None);
        var cash = chart.Get("1010");
        var revenue = chart.Get("9010");

        cash.DimensionRules.Should().HaveCount(4);
        var dimIds = cash.DimensionRules.Select(r => r.DimensionId).ToArray();

        var s1 = Guid.CreateVersion7();
        var s2 = Guid.CreateVersion7();
        var s3 = Guid.CreateVersion7();
        var v4a = Guid.CreateVersion7();
        var v4b = Guid.CreateVersion7();

        var bagA = new DimensionBag([
            new DimensionValue(dimIds[0], s1),
            new DimensionValue(dimIds[1], s2),
            new DimensionValue(dimIds[2], s3),
            new DimensionValue(dimIds[3], v4a)
        ]);

        var bagB = new DimensionBag([
            new DimensionValue(dimIds[0], s1),
            new DimensionValue(dimIds[1], s2),
            new DimensionValue(dimIds[2], s3),
            new DimensionValue(dimIds[3], v4b)
        ]);

        return (cash.Id, revenue.Id, bagA, bagB, s1, s2, s3, dimIds[3], v4a, v4b);
    }

    private static async Task PostTwoCashDebitsAsync(
        IHost host,
        Guid documentId,
        DateTime dtUtc,
        DimensionBag bagA,
        DimensionBag bagB)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

        await posting.PostAsync(
            PostingOperation.Post,
            async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                var cash = chart.Get("1010");
                var revenue = chart.Get("9010");

                ctx.Post(documentId, dtUtc, cash, revenue, 100m, debitDimensions: bagA, creditDimensions: DimensionBag.Empty);
                ctx.Post(documentId, dtUtc, cash, revenue, 200m, debitDimensions: bagB, creditDimensions: DimensionBag.Empty);
            },
            manageTransaction: true,
            ct: CancellationToken.None);
    }
}