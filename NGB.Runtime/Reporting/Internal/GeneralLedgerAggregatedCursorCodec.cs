using System.Text.Json.Serialization;
using NGB.Accounting.Reports.GeneralLedgerAggregated;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Reporting.Internal;

internal static class GeneralLedgerAggregatedCursorCodec
{
    private const string CursorKind = "accounting.general_ledger_aggregated";

    public static string Encode(GeneralLedgerAggregatedReportCursor cursor)
    {
        if (cursor is null)
            throw new NgbArgumentRequiredException(nameof(cursor));

        var totals = cursor.TotalDebit is { } debit && cursor.TotalCredit is { } credit && cursor.ClosingBalance is { } closing
            ? new Totals(debit, credit, closing)
            : null;

        return SpecializedReportCursorCodec.Encode(CursorKind, new Payload(
            cursor.AfterPeriodUtc, cursor.AfterDocumentId, cursor.AfterCounterAccountCode, cursor.AfterCounterAccountId, cursor.AfterDimensionSetId, cursor.RunningBalance,
            cursor.SnapshotId, totals));
    }

    public static GeneralLedgerAggregatedReportCursor Decode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new NgbArgumentInvalidException("cursor", "Invalid cursor format.");

        Payload payload;
        try
        {
            payload = SpecializedReportCursorCodec.Decode<Payload>(CursorKind, value);
        }
        catch (NgbArgumentInvalidException)
        {
            throw new NgbArgumentInvalidException("cursor", "Invalid cursor format or report version.");
        }

        if (payload.AfterCounterAccountCode is null)
            throw new NgbArgumentInvalidException("cursor", "Invalid cursor counter account code.");

        return new GeneralLedgerAggregatedReportCursor
        {
            AfterPeriodUtc = payload.AfterPeriodUtc.Kind == DateTimeKind.Local
                ? payload.AfterPeriodUtc.ToUniversalTime()
                : DateTime.SpecifyKind(payload.AfterPeriodUtc, DateTimeKind.Utc),
            AfterDocumentId = payload.AfterDocumentId,
            AfterCounterAccountCode = payload.AfterCounterAccountCode,
            AfterCounterAccountId = payload.AfterCounterAccountId,
            AfterDimensionSetId = payload.AfterDimensionSetId,
            RunningBalance = payload.RunningBalance,
            SnapshotId = payload.SnapshotId,
            TotalDebit = payload.Totals?.TotalDebit,
            TotalCredit = payload.Totals?.TotalCredit,
            ClosingBalance = payload.Totals?.ClosingBalance
        };
    }

    private sealed record Totals(
        [property: JsonRequired] decimal TotalDebit,
        [property: JsonRequired] decimal TotalCredit,
        [property: JsonRequired] decimal ClosingBalance);

    private sealed record Payload(
        [property: JsonRequired] DateTime AfterPeriodUtc,
        [property: JsonRequired] Guid AfterDocumentId,
        [property: JsonRequired] string AfterCounterAccountCode,
        [property: JsonRequired] Guid AfterCounterAccountId,
        [property: JsonRequired] Guid AfterDimensionSetId,
        [property: JsonRequired] decimal RunningBalance,
        [property: JsonRequired] Guid SnapshotId,
        [property: JsonRequired] Totals? Totals);
}
