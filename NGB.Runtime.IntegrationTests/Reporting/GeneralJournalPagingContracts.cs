using FluentAssertions;
using NGB.Accounting.Reports.GeneralJournal;

namespace NGB.Runtime.IntegrationTests.Reporting;

/// <summary>
/// Shared contract assertions for General Journal keyset pagination.
///
/// These contracts catch common paging regressions:
/// - duplicates / cursor not advancing
/// - unstable ordering
/// - NextCursor not matching the last row key
/// - "HasMore" / empty-page inconsistencies that can lead to infinite loops
/// </summary>
internal static class GeneralJournalPagingContracts
{
    public static void AssertPageContracts(GeneralJournalPage page, GeneralJournalCursor? requestCursor) =>
        AssertCoreContracts(page.Lines, page.HasMore, page.NextCursor, requestCursor);

    private static void AssertCoreContracts(
        IReadOnlyList<GeneralJournalLine> lines,
        bool hasMore,
        GeneralJournalCursor? nextCursor,
        GeneralJournalCursor? requestCursor)
    {
        // If there are lines, they must be ordered by (PeriodUtc, EntryId).
        if (lines.Count > 0)
        {
            var ordered = lines
                .OrderBy(l => l.PeriodUtc)
                .ThenBy(l => l.EntryId)
                .ToArray();

            lines.Should().Equal(ordered, "General Journal paging must return stable keyset order");

            if (requestCursor is not null)
            {
                var first = lines[0];
                IsAfter(first.PeriodUtc, first.EntryId, requestCursor)
                    .Should()
                    .BeTrue("the first row of a page must be strictly after the request cursor");
            }
        }

        if (hasMore)
        {
            lines.Should().NotBeEmpty("HasMore=true must not return an empty page (infinite loop hazard)");

            nextCursor.Should().NotBeNull("HasMore=true must include NextCursor");
            var last = lines[^1];

            nextCursor!.AfterPeriodUtc.Should().Be(last.PeriodUtc, "NextCursor must match the last row PeriodUtc");
            nextCursor.AfterEntryId.Should().Be(last.EntryId, "NextCursor must match the last row EntryId");

            if (requestCursor is not null)
            {
                IsAfter(nextCursor.AfterPeriodUtc, nextCursor.AfterEntryId, requestCursor)
                    .Should()
                    .BeTrue("NextCursor must strictly advance relative to the request cursor");
            }
        }
        else
        {
            // End of dataset: no need for a cursor.
            nextCursor.Should().BeNull("HasMore=false should not provide a NextCursor");
        }
    }

    private static bool IsAfter(DateTime periodUtc, long entryId, GeneralJournalCursor cursor)
    {
        if (periodUtc > cursor.AfterPeriodUtc) return true;
        if (periodUtc < cursor.AfterPeriodUtc) return false;
        return entryId > cursor.AfterEntryId;
    }
}
