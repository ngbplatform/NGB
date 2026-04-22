using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Reports.GeneralJournal;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

/// <summary>
/// Keyset paging stability for General Journal.
/// Fetching next page with the same cursor should return the same lines even if new
/// entries are inserted *before* the cursor position (i.e. they sort earlier than the cursor key).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class GeneralJournal_Paging_Cursor_Stability_EndToEnd_P2Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task GeneralJournalReader_Paging_IsStable_WhenNewEntriesInsertedBeforeCursor()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        // Create 5 postings so we have at least 3 pages with page size 2.
        var baseDay = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 5; i++)
            await ReportingTestHelpers.PostAsync(host, Guid.CreateVersion7(), baseDay.AddDays(i), "50", "90.1", 10m + i);

        GeneralJournalPage page1;
        GeneralJournalPage page2Before;

        // Fetch first page.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IGeneralJournalReader>();

            page1 = await reader.GetPageAsync(new GeneralJournalPageRequest
            {
                FromInclusive = ReportingTestHelpers.Period,
                ToInclusive = ReportingTestHelpers.Period,
                PageSize = 2,
                Cursor = null
            }, CancellationToken.None);

            page1.Lines.Should().HaveCount(2);
            page1.HasMore.Should().BeTrue();
            page1.NextCursor.Should().NotBeNull();
        }

        // Fetch second page (baseline).
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IGeneralJournalReader>();

            page2Before = await reader.GetPageAsync(new GeneralJournalPageRequest
            {
                FromInclusive = ReportingTestHelpers.Period,
                ToInclusive = ReportingTestHelpers.Period,
                PageSize = 2,
                Cursor = page1.NextCursor
            }, CancellationToken.None);

            page2Before.Lines.Should().HaveCount(2);
        }

        // Insert a new posting BEFORE the cursor position: use an earlier PeriodUtc (day 1 of month).
        // This should not affect the composition of page 2 when using the same cursor.
        await ReportingTestHelpers.PostAsync(host, Guid.CreateVersion7(), ReportingTestHelpers.Day1Utc, "50", "90.1", 999m);

        // Fetch second page again with the SAME cursor.
        GeneralJournalPage page2After;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IGeneralJournalReader>();

            page2After = await reader.GetPageAsync(new GeneralJournalPageRequest
            {
                FromInclusive = ReportingTestHelpers.Period,
                ToInclusive = ReportingTestHelpers.Period,
                PageSize = 2,
                Cursor = page1.NextCursor
            }, CancellationToken.None);
        }

        page2After.Lines.Select(l => l.EntryId).Should().Equal(page2Before.Lines.Select(l => l.EntryId));
        page2After.Lines.Select(l => l.DocumentId).Should().Equal(page2Before.Lines.Select(l => l.DocumentId));
    }

    [Fact]
    public async Task GeneralJournalReportReader_Paging_IsStable_WhenNewEntriesInsertedBeforeCursor()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        // Create 5 postings so we have at least 3 pages with page size 2.
        var baseDay = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 5; i++)
            await ReportingTestHelpers.PostAsync(host, Guid.CreateVersion7(), baseDay.AddDays(i), "50", "90.1", 10m + i);

        GeneralJournalPage page1;
        GeneralJournalPage page2Before;

        // Fetch first page.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IGeneralJournalReportReader>();

            page1 = await reader.GetPageAsync(new GeneralJournalPageRequest
            {
                FromInclusive = ReportingTestHelpers.Period,
                ToInclusive = ReportingTestHelpers.Period,
                PageSize = 2,
                Cursor = null
            }, CancellationToken.None);

            page1.Lines.Should().HaveCount(2);
            page1.HasMore.Should().BeTrue();
            page1.NextCursor.Should().NotBeNull();
        }

        // Fetch second page (baseline).
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IGeneralJournalReportReader>();

            page2Before = await reader.GetPageAsync(new GeneralJournalPageRequest
            {
                FromInclusive = ReportingTestHelpers.Period,
                ToInclusive = ReportingTestHelpers.Period,
                PageSize = 2,
                Cursor = page1.NextCursor
            }, CancellationToken.None);

            page2Before.Lines.Should().HaveCount(2);
        }

        // Insert a new posting BEFORE the cursor position.
        await ReportingTestHelpers.PostAsync(host, Guid.CreateVersion7(), ReportingTestHelpers.Day1Utc, "50", "90.1", 999m);

        // Fetch second page again with the SAME cursor.
        GeneralJournalPage page2After;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IGeneralJournalReportReader>();

            page2After = await reader.GetPageAsync(new GeneralJournalPageRequest
            {
                FromInclusive = ReportingTestHelpers.Period,
                ToInclusive = ReportingTestHelpers.Period,
                PageSize = 2,
                Cursor = page1.NextCursor
            }, CancellationToken.None);
        }

        page2After.Lines.Select(l => l.EntryId).Should().Equal(page2Before.Lines.Select(l => l.EntryId));
        page2After.Lines.Select(l => l.DocumentId).Should().Equal(page2Before.Lines.Select(l => l.DocumentId));
    }
}
