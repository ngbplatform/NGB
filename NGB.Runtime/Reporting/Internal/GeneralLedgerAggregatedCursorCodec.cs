using System.Globalization;
using NGB.Accounting.Reports.GeneralLedgerAggregated;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Reporting.Internal;

internal static class GeneralLedgerAggregatedCursorCodec
{
    // Legacy format:
    // {afterPeriodUtc:O}|{afterDocumentId}|{afterCounterAccountCode}|{afterCounterAccountId}|{afterDimensionSetId}|{runningBalance:G29}
    //
    // Current format appends cursor-carried totals to avoid re-reading turnovers on cursor pages:
    // {afterPeriodUtc:O}|{afterDocumentId}|{afterCounterAccountCode}|{afterCounterAccountId}|{afterDimensionSetId}|{runningBalance:G29}|{totalDebit:G29}|{totalCredit:G29}|{closingBalance:G29}
    public static string Encode(GeneralLedgerAggregatedReportCursor cursor)
    {
        if (cursor.TotalDebit is null || cursor.TotalCredit is null || cursor.ClosingBalance is null)
        {
            return string.Join('|',
                cursor.AfterPeriodUtc.ToString("O", CultureInfo.InvariantCulture),
                cursor.AfterDocumentId.ToString("D"),
                Uri.EscapeDataString(cursor.AfterCounterAccountCode),
                cursor.AfterCounterAccountId.ToString("D"),
                cursor.AfterDimensionSetId.ToString("D"),
                cursor.RunningBalance.ToString(CultureInfo.InvariantCulture));
        }

        return string.Join('|',
            cursor.AfterPeriodUtc.ToString("O", CultureInfo.InvariantCulture),
            cursor.AfterDocumentId.ToString("D"),
            Uri.EscapeDataString(cursor.AfterCounterAccountCode),
            cursor.AfterCounterAccountId.ToString("D"),
            cursor.AfterDimensionSetId.ToString("D"),
            cursor.RunningBalance.ToString(CultureInfo.InvariantCulture),
            cursor.TotalDebit.Value.ToString(CultureInfo.InvariantCulture),
            cursor.TotalCredit.Value.ToString(CultureInfo.InvariantCulture),
            cursor.ClosingBalance.Value.ToString(CultureInfo.InvariantCulture));
    }

    public static GeneralLedgerAggregatedReportCursor Decode(string value)
    {
        var parts = value.Split('|');
        if (parts.Length is not (6 or 9))
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

        if (!Guid.TryParse(parts[1], out var afterDocumentId))
            throw new NgbArgumentInvalidException("cursor", "Invalid cursor document id.");

        var afterCounterAccountCode = Uri.UnescapeDataString(parts[2]);

        if (!Guid.TryParse(parts[3], out var afterCounterAccountId))
            throw new NgbArgumentInvalidException("cursor", "Invalid cursor counter account id.");

        if (!Guid.TryParse(parts[4], out var afterDimensionSetId))
            throw new NgbArgumentInvalidException("cursor", "Invalid cursor dimension set id.");

        if (!decimal.TryParse(parts[5], NumberStyles.Number, CultureInfo.InvariantCulture, out var runningBalance))
            throw new NgbArgumentInvalidException("cursor", "Invalid cursor running balance.");

        decimal? totalDebit = null;
        decimal? totalCredit = null;
        decimal? closingBalance = null;

        if (parts.Length == 9)
        {
            if (!decimal.TryParse(parts[6], NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedTotalDebit))
                throw new NgbArgumentInvalidException("cursor", "Invalid cursor total debit.");

            if (!decimal.TryParse(parts[7], NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedTotalCredit))
                throw new NgbArgumentInvalidException("cursor", "Invalid cursor total credit.");

            if (!decimal.TryParse(parts[8], NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedClosingBalance))
                throw new NgbArgumentInvalidException("cursor", "Invalid cursor closing balance.");

            totalDebit = parsedTotalDebit;
            totalCredit = parsedTotalCredit;
            closingBalance = parsedClosingBalance;
        }

        return new GeneralLedgerAggregatedReportCursor
        {
            AfterPeriodUtc = afterPeriodUtc,
            AfterDocumentId = afterDocumentId,
            AfterCounterAccountCode = afterCounterAccountCode,
            AfterCounterAccountId = afterCounterAccountId,
            AfterDimensionSetId = afterDimensionSetId,
            RunningBalance = runningBalance,
            TotalDebit = totalDebit,
            TotalCredit = totalCredit,
            ClosingBalance = closingBalance
        };
    }
}
