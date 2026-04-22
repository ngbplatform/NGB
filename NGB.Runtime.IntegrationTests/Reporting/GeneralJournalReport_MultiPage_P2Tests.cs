using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Reports.GeneralJournal;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

[Collection(PostgresCollection.Name)]
public sealed class GeneralJournalReport_MultiPage_P2Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task GeneralJournalReport_MultiPage_Read_IsStableAcrossPages_AndMatchesFullPageSum()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        // Arrange: multiple docs so that we have multiple pages.
        var expected = 0m;
        for (var i = 0; i < 17; i++)
        {
            var amount = 1m + i;
            expected += amount;
            await ReportingTestHelpers.PostAsync(host, Guid.CreateVersion7(), ReportingTestHelpers.Day1Utc, "50", "90.1", amount);
        }

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IGeneralJournalReportReader>();

        GeneralJournalCursor? cursor = null;

        var ids = new HashSet<long>();
        var orderedEntryIds = new List<long>();
        var sum = 0m;

        for (var guard = 0; guard < 10_000; guard++)
        {
            var requestCursor = cursor;
            var page = await reader.GetPageAsync(new GeneralJournalPageRequest
            {
                FromInclusive = ReportingTestHelpers.Period,
                ToInclusive = ReportingTestHelpers.Period,
                PageSize = 2,
                Cursor = cursor
            }, CancellationToken.None);

            GeneralJournalPagingContracts.AssertPageContracts(page, requestCursor);

            foreach (var l in page.Lines)
            {
                ids.Add(l.EntryId);
                orderedEntryIds.Add(l.EntryId);
                sum += l.Amount;
            }

            if (!page.HasMore)
                break;

            page.NextCursor.Should().NotBeNull();
            cursor = page.NextCursor;
        }

        ids.Count.Should().BeGreaterThan(2);
        sum.Should().Be(expected);

        // Sorted by (PeriodUtc, EntryId). In this test all rows share the same PeriodUtc, so EntryId must be increasing.
        orderedEntryIds.Should().BeInAscendingOrder();
        orderedEntryIds.Distinct().Should().HaveCount(orderedEntryIds.Count);
    }
}
