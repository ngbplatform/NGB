using System.Globalization;
using NGB.Accounting.Reports.GeneralJournal;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Reporting.Internal;

internal static class GeneralJournalCursorCodec
{
    // Format: {afterPeriodUtc:O}|{afterEntryId}
    public static string Encode(GeneralJournalCursor cursor) => $"{cursor.AfterPeriodUtc:O}|{cursor.AfterEntryId}";

    public static GeneralJournalCursor Decode(string value)
    {
        var parts = value.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
            throw new NgbArgumentInvalidException("cursor", "Invalid cursor format.");

        if (!DateTime.TryParse(
                parts[0],
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var afterPeriodUtc))
        {
            throw new NgbArgumentInvalidException("cursor", "Invalid cursor timestamp.");
        }
        afterPeriodUtc = DateTime.SpecifyKind(afterPeriodUtc, DateTimeKind.Utc);

        if (!long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var afterEntryId))
            throw new NgbArgumentInvalidException("cursor", "Invalid cursor entry id.");

        return new GeneralJournalCursor(afterPeriodUtc, afterEntryId);
    }
}
