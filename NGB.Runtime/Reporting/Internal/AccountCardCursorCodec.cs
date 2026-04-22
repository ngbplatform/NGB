using System.Globalization;
using NGB.Accounting.Reports.AccountCard;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Reporting.Internal;

internal static class AccountCardCursorCodec
{
    // Backward-compatible formats:
    // v1: {afterPeriodUtc:O}|{afterEntryId}|{runningBalance:G29}
    // v2: {afterPeriodUtc:O}|{afterEntryId}|{runningBalance:G29}|{totalDebit:G29}|{totalCredit:G29}|{closingBalance:G29}
    public static string Encode(AccountCardReportCursor cursor)
    {
        if (cursor.TotalDebit is null || cursor.TotalCredit is null || cursor.ClosingBalance is null)
            return $"{cursor.AfterPeriodUtc:O}|{cursor.AfterEntryId}|{cursor.RunningBalance.ToString(CultureInfo.InvariantCulture)}";

        return string.Join(
            '|',
            cursor.AfterPeriodUtc.ToString("O", CultureInfo.InvariantCulture),
            cursor.AfterEntryId.ToString(CultureInfo.InvariantCulture),
            cursor.RunningBalance.ToString(CultureInfo.InvariantCulture),
            cursor.TotalDebit.Value.ToString(CultureInfo.InvariantCulture),
            cursor.TotalCredit.Value.ToString(CultureInfo.InvariantCulture),
            cursor.ClosingBalance.Value.ToString(CultureInfo.InvariantCulture));
    }

    public static AccountCardReportCursor Decode(string value)
    {
        var parts = value.Split('|', StringSplitOptions.TrimEntries);
        if (parts.Length is not 3 and not 6)
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

        if (!decimal.TryParse(parts[2], NumberStyles.Number, CultureInfo.InvariantCulture, out var running))
            throw new NgbArgumentInvalidException("cursor", "Invalid cursor running balance.");

        decimal? totalDebit = null;
        decimal? totalCredit = null;
        decimal? closingBalance = null;

        if (parts.Length == 6)
        {
            if (!decimal.TryParse(parts[3], NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedTotalDebit))
                throw new NgbArgumentInvalidException("cursor", "Invalid cursor total debit.");

            if (!decimal.TryParse(parts[4], NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedTotalCredit))
                throw new NgbArgumentInvalidException("cursor", "Invalid cursor total credit.");

            if (!decimal.TryParse(parts[5], NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedClosing))
                throw new NgbArgumentInvalidException("cursor", "Invalid cursor closing balance.");

            totalDebit = parsedTotalDebit;
            totalCredit = parsedTotalCredit;
            closingBalance = parsedClosing;
        }

        return new AccountCardReportCursor
        {
            AfterPeriodUtc = afterPeriodUtc,
            AfterEntryId = afterEntryId,
            RunningBalance = running,
            TotalDebit = totalDebit,
            TotalCredit = totalCredit,
            ClosingBalance = closingBalance
        };
    }
}
