using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Reports.GeneralLedgerAggregated;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

[Collection(PostgresCollection.Name)]
public sealed class GeneralLedgerAggregated_Paging_Cursor_Stability_EndToEnd_P2Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task GeneralLedgerAggregatedReportReader_Pages_Through_Large_Dataset_Without_Gaps_And_With_Running_Continuity()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (cashId, _, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        var baseDay = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc);
        var expectedTotal = 0m;
        for (var i = 0; i < 25; i++)
        {
            var amount = 10m + i;
            expectedTotal += amount;
            await ReportingTestHelpers.PostAsync(host, Guid.CreateVersion7(), baseDay.AddDays(i), "50", "90.1", amount);
        }

        var identities = new List<string>();
        var running = 0m;
        GeneralLedgerAggregatedReportCursor? cursor = null;
        var pageIndex = 0;
        GeneralLedgerAggregatedReportPage? lastPage = null;

        while (true)
        {
            await using var scope = host.Services.CreateAsyncScope();
            var reader = scope.ServiceProvider.GetRequiredService<IGeneralLedgerAggregatedPagedReportReader>();
            var page = await reader.GetPageAsync(
                new GeneralLedgerAggregatedReportPageRequest
                {
                    AccountId = cashId,
                    FromInclusive = ReportingTestHelpers.Period,
                    ToInclusive = ReportingTestHelpers.Period,
                    PageSize = 7,
                    Cursor = cursor
                },
                CancellationToken.None);

            if (pageIndex == 0)
                page.OpeningBalance.Should().Be(0m);
            else
                page.OpeningBalance.Should().Be(running);

            page.Lines.Should().NotBeEmpty();
            foreach (var line in page.Lines)
            {
                running += line.DebitAmount - line.CreditAmount;
                line.RunningBalance.Should().Be(running);
                identities.Add(GetIdentity(line));
            }

            lastPage = page;
            pageIndex++;

            if (!page.HasMore || page.NextCursor is null)
                break;

            cursor = page.NextCursor;
        }

        identities.Should().HaveCount(25);
        identities.Should().OnlyHaveUniqueItems();
        running.Should().Be(expectedTotal);
        lastPage.Should().NotBeNull();
        lastPage!.TotalDebit.Should().Be(expectedTotal);
        lastPage.TotalCredit.Should().Be(0m);
        lastPage.ClosingBalance.Should().Be(expectedTotal);
    }

    [Fact]
    public async Task GeneralLedgerAggregatedReportReader_Paging_IsStable_When_New_Rows_Are_Inserted_Before_The_Cursor()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (cashId, _, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        var baseDay = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 5; i++)
            await ReportingTestHelpers.PostAsync(host, Guid.CreateVersion7(), baseDay.AddDays(i), "50", "90.1", 10m + i);

        GeneralLedgerAggregatedReportPage page1;
        GeneralLedgerAggregatedReportPage page2Before;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IGeneralLedgerAggregatedPagedReportReader>();
            page1 = await reader.GetPageAsync(
                new GeneralLedgerAggregatedReportPageRequest
                {
                    AccountId = cashId,
                    FromInclusive = ReportingTestHelpers.Period,
                    ToInclusive = ReportingTestHelpers.Period,
                    PageSize = 2
                },
                CancellationToken.None);

            page1.Lines.Should().HaveCount(2);
            page1.HasMore.Should().BeTrue();
            page1.NextCursor.Should().NotBeNull();
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IGeneralLedgerAggregatedPagedReportReader>();
            page2Before = await reader.GetPageAsync(
                new GeneralLedgerAggregatedReportPageRequest
                {
                    AccountId = cashId,
                    FromInclusive = ReportingTestHelpers.Period,
                    ToInclusive = ReportingTestHelpers.Period,
                    PageSize = 2,
                    Cursor = page1.NextCursor
                },
                CancellationToken.None);
        }

        await ReportingTestHelpers.PostAsync(host, Guid.CreateVersion7(), ReportingTestHelpers.Day1Utc, "50", "90.1", 999m);

        GeneralLedgerAggregatedReportPage page2After;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IGeneralLedgerAggregatedPagedReportReader>();
            page2After = await reader.GetPageAsync(
                new GeneralLedgerAggregatedReportPageRequest
                {
                    AccountId = cashId,
                    FromInclusive = ReportingTestHelpers.Period,
                    ToInclusive = ReportingTestHelpers.Period,
                    PageSize = 2,
                    Cursor = page1.NextCursor
                },
                CancellationToken.None);
        }

        page2After.OpeningBalance.Should().Be(page2Before.OpeningBalance);
        page2After.Lines.Select(GetIdentity).Should().Equal(page2Before.Lines.Select(GetIdentity));
        page2After.Lines.Select(x => x.RunningBalance).Should().Equal(page2Before.Lines.Select(x => x.RunningBalance));
    }

    private static string GetIdentity(GeneralLedgerAggregatedReportLine line)
        => $"{line.PeriodUtc:O}|{line.DocumentId:D}|{line.CounterAccountCode}|{line.CounterAccountId:D}|{line.DimensionSetId:D}";
}
