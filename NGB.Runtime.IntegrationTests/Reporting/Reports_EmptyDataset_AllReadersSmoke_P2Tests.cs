using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Reports.AccountCard;
using NGB.Accounting.Reports.BalanceSheet;
using NGB.Accounting.Reports.GeneralJournal;
using NGB.Accounting.Reports.IncomeStatement;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

[Collection(PostgresCollection.Name)]
public sealed class Reports_EmptyDataset_AllReadersSmoke_P2Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task AllReaders_ReturnValidEmptyResults_WhenNoPostingsExist()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, _, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        // Trial Balance
        var tb = await sp.GetRequiredService<ITrialBalanceReader>()
            .GetAsync(ReportingTestHelpers.Period, ReportingTestHelpers.Period, CancellationToken.None);
        tb.Should().BeEmpty();

        // General Journal (reader)
        var gjPage = await sp.GetRequiredService<IGeneralJournalReader>()
            .GetPageAsync(new GeneralJournalPageRequest
            {
                FromInclusive = ReportingTestHelpers.Period,
                ToInclusive = ReportingTestHelpers.Period,
                PageSize = 10
            }, CancellationToken.None);
        gjPage.Lines.Should().BeEmpty();
        gjPage.HasMore.Should().BeFalse();

        // General Journal (report)
        var gjReportPage = await sp.GetRequiredService<IGeneralJournalReportReader>()
            .GetPageAsync(new GeneralJournalPageRequest
            {
                FromInclusive = ReportingTestHelpers.Period,
                ToInclusive = ReportingTestHelpers.Period,
                PageSize = 10
            }, CancellationToken.None);
        gjReportPage.Lines.Should().BeEmpty();
        gjReportPage.HasMore.Should().BeFalse();
        gjReportPage.NextCursor.Should().BeNull();

        // Account Card (paged report)
        var paged = await sp.GetRequiredService<IAccountCardEffectivePagedReportReader>()
            .GetPageAsync(new AccountCardReportPageRequest
            {
                AccountId = cashId,
                FromInclusive = ReportingTestHelpers.Period,
                ToInclusive = ReportingTestHelpers.Period,
                PageSize = 10
            }, CancellationToken.None);
        paged.Lines.Should().BeEmpty();
        paged.OpeningBalance.Should().Be(0);
        paged.ClosingBalance.Should().Be(0);

        // Balance Sheet
        var bs = await sp.GetRequiredService<IBalanceSheetReportReader>()
            .GetAsync(new BalanceSheetReportRequest { AsOfPeriod = ReportingTestHelpers.Period }, CancellationToken.None);
        bs.TotalAssets.Should().Be(0);
        bs.TotalLiabilities.Should().Be(0);
        bs.TotalEquity.Should().Be(0);

        // Income Statement
        var isr = await sp.GetRequiredService<IIncomeStatementReportReader>()
            .GetAsync(new IncomeStatementReportRequest
            {
                FromInclusive = ReportingTestHelpers.Period,
                ToInclusive = ReportingTestHelpers.Period,
                IncludeZeroLines = false
            }, CancellationToken.None);
        isr.Sections.SelectMany(s => s.Lines).Should().BeEmpty();
        isr.NetIncome.Should().Be(0);

        // General Ledger aggregated
        var gl = await ReportingTestHelpers.ReadAllGeneralLedgerAggregatedLinesAsync(
            sp,
            cashId,
            ReportingTestHelpers.Period,
            ReportingTestHelpers.Period,
            ct: CancellationToken.None);
        gl.Should().BeEmpty();

        // Accounting consistency report (no turnovers, no balances) should be OK.
        var cr = await sp.GetRequiredService<IAccountingConsistencyReportReader>()
            .RunForPeriodAsync(ReportingTestHelpers.Period, previousPeriodForChainCheck: null, CancellationToken.None);
        cr.IsOk.Should().BeTrue();
    }
}
