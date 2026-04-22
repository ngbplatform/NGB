using FluentAssertions;
using NGB.Accounting.Reports.AccountCard;

namespace NGB.Runtime.IntegrationTests.Reporting;

internal static class AccountCardPagingContracts
{
    public static void AssertLinePageContract(AccountCardLinePage page, AccountCardLineCursor? requestCursor)
    {
        page.Lines.Should().NotBeNull();

        // Sorted by keyset (PeriodUtc ASC, EntryId ASC).
        for (var i = 1; i < page.Lines.Count; i++)
        {
            var prev = page.Lines[i - 1];
            var cur = page.Lines[i];

            var ok = prev.PeriodUtc < cur.PeriodUtc ||
                     (prev.PeriodUtc == cur.PeriodUtc && prev.EntryId < cur.EntryId);

            ok.Should().BeTrue("AccountCardLinePage must be sorted by (PeriodUtc, EntryId) ASC");
        }

        if (requestCursor is not null && page.Lines.Count > 0)
        {
            var first = page.Lines[0];
            IsAfter(first.PeriodUtc, first.EntryId, requestCursor)
                .Should().BeTrue("First line must be strictly after request cursor");
        }

        if (page.HasMore)
        {
            page.Lines.Should().NotBeEmpty("HasMore=true implies at least one line");
            page.NextCursor.Should().NotBeNull("HasMore=true implies NextCursor is set");

            var last = page.Lines[^1];
            page.NextCursor!.AfterPeriodUtc.Should().Be(last.PeriodUtc, "NextCursor must match last PeriodUtc");
            page.NextCursor!.AfterEntryId.Should().Be(last.EntryId, "NextCursor must match last EntryId");

            if (requestCursor is not null)
            {
                IsAfter(page.NextCursor.AfterPeriodUtc, page.NextCursor.AfterEntryId, requestCursor)
                    .Should().BeTrue("NextCursor must move forward relative to request cursor");
            }
        }
        else
        {
            page.NextCursor.Should().BeNull("HasMore=false implies NextCursor is null");
        }
    }

    public static void AssertReportPageContract(AccountCardReportPage page, AccountCardReportCursor? requestCursor)
    {
        page.Lines.Should().NotBeNull();

        // Sorted by keyset (PeriodUtc ASC, EntryId ASC).
        for (var i = 1; i < page.Lines.Count; i++)
        {
            var prev = page.Lines[i - 1];
            var cur = page.Lines[i];

            var ok = prev.PeriodUtc < cur.PeriodUtc ||
                     (prev.PeriodUtc == cur.PeriodUtc && prev.EntryId < cur.EntryId);

            ok.Should().BeTrue("AccountCardReportPage must be sorted by (PeriodUtc, EntryId) ASC");
        }

        if (requestCursor is not null && page.Lines.Count > 0)
        {
            var first = page.Lines[0];
            IsAfter(first.PeriodUtc, first.EntryId, requestCursor)
                .Should().BeTrue("First line must be strictly after request cursor");

            // Currency invariant: OpeningBalance must equal cursor.RunningBalance.
            page.OpeningBalance.Should().Be(requestCursor.RunningBalance, "cursor carries running balance currency");
        }

        if (page.HasMore)
        {
            page.Lines.Should().NotBeEmpty("HasMore=true implies at least one line");
            page.NextCursor.Should().NotBeNull("HasMore=true implies NextCursor is set");

            var last = page.Lines[^1];
            page.NextCursor!.AfterPeriodUtc.Should().Be(last.PeriodUtc, "NextCursor must match last PeriodUtc");
            page.NextCursor!.AfterEntryId.Should().Be(last.EntryId, "NextCursor must match last EntryId");
            page.NextCursor!.RunningBalance.Should().Be(last.RunningBalance, "NextCursor must carry last running balance");

            if (requestCursor is not null)
            {
                IsAfter(page.NextCursor.AfterPeriodUtc, page.NextCursor.AfterEntryId, requestCursor)
                    .Should().BeTrue("NextCursor must move forward relative to request cursor");
            }
        }
        else
        {
            page.NextCursor.Should().BeNull("HasMore=false implies NextCursor is null");
        }
    }

    private static bool IsAfter(DateTime periodUtc, long entryId, AccountCardLineCursor cursor)
    {
        if (periodUtc > cursor.AfterPeriodUtc) return true;
        if (periodUtc < cursor.AfterPeriodUtc) return false;
        return entryId > cursor.AfterEntryId;
    }

    private static bool IsAfter(DateTime periodUtc, long entryId, AccountCardReportCursor cursor)
    {
        if (periodUtc > cursor.AfterPeriodUtc) return true;
        if (periodUtc < cursor.AfterPeriodUtc) return false;
        return entryId > cursor.AfterEntryId;
    }
}
