using System.Globalization;
using NGB.Accounting.Reports.LedgerAnalysis;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Reporting.Internal;

internal static class LedgerAnalysisDetailCursorCodec
{
    public static string Encode(LedgerAnalysisFlatDetailCursor cursor)
        => string.Join(
            '|',
            cursor.AfterPeriodUtc.ToString("O", CultureInfo.InvariantCulture),
            cursor.AfterEntryId.ToString(CultureInfo.InvariantCulture),
            Uri.EscapeDataString(cursor.AfterPostingSide));

    public static LedgerAnalysisFlatDetailCursor Decode(string value)
    {
        var parts = value.Split('|', StringSplitOptions.TrimEntries);
        if (parts.Length != 3)
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

        var afterPostingSide = Uri.UnescapeDataString(parts[2]);
        if (string.IsNullOrWhiteSpace(afterPostingSide))
            throw new NgbArgumentInvalidException("cursor", "Invalid cursor posting side.");

        return new LedgerAnalysisFlatDetailCursor(afterPeriodUtc, afterEntryId, afterPostingSide);
    }
}
