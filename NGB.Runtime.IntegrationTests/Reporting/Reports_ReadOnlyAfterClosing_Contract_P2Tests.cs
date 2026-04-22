using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Reports.BalanceSheet;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

[Collection(PostgresCollection.Name)]
public sealed class Reports_ReadOnlyAfterClosing_Contract_P2Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task TrialBalance_And_BalanceSheet_AreStableBeforeAndAfterCloseMonth()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        await ReportingTestHelpers.PostAsync(host, Guid.CreateVersion7(), ReportingTestHelpers.Day1Utc, "50", "90.1", 100m);
        await ReportingTestHelpers.PostAsync(host, Guid.CreateVersion7(), ReportingTestHelpers.Day2Utc, "91", "50", 40m);

        await using var scope1 = host.Services.CreateAsyncScope();
        var tbReader = scope1.ServiceProvider.GetRequiredService<ITrialBalanceReader>();
        var bsReader = scope1.ServiceProvider.GetRequiredService<IBalanceSheetReportReader>();

        var tbBefore = await tbReader.GetAsync(ReportingTestHelpers.Period, ReportingTestHelpers.Period, CancellationToken.None);
        var bsBefore = await bsReader.GetAsync(new BalanceSheetReportRequest { AsOfPeriod = ReportingTestHelpers.Period }, CancellationToken.None);

        await ReportingTestHelpers.CloseMonthAsync(host, ReportingTestHelpers.Period);

        await using var scope2 = host.Services.CreateAsyncScope();
        var tbAfter = await scope2.ServiceProvider.GetRequiredService<ITrialBalanceReader>()
            .GetAsync(ReportingTestHelpers.Period, ReportingTestHelpers.Period, CancellationToken.None);
        var bsAfter = await scope2.ServiceProvider.GetRequiredService<IBalanceSheetReportReader>()
            .GetAsync(new BalanceSheetReportRequest { AsOfPeriod = ReportingTestHelpers.Period }, CancellationToken.None);

        // Trial balance should be identical for the same period.
        tbAfter.Should().BeEquivalentTo(tbBefore, options => options.WithStrictOrdering());

        // Balance Sheet high-level totals should be identical.
        bsAfter.TotalAssets.Should().Be(bsBefore.TotalAssets);
        bsAfter.TotalLiabilities.Should().Be(bsBefore.TotalLiabilities);
        bsAfter.TotalEquity.Should().Be(bsBefore.TotalEquity);
        bsAfter.Difference.Should().Be(bsBefore.Difference);
    }
}
