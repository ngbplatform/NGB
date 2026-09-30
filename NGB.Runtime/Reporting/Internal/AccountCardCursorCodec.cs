using System.Text.Json.Serialization;
using NGB.Accounting.Reports.AccountCard;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Reporting.Internal;

internal static class AccountCardCursorCodec
{
    private const string CursorKind = "accounting.account_card";

    public static string Encode(AccountCardReportCursor cursor)
    {
        if (cursor is null)
            throw new NgbArgumentRequiredException(nameof(cursor));

        var totals = cursor is { TotalDebit: { } debit, TotalCredit: { } credit, ClosingBalance: { } closing }
            ? new Totals(debit, credit, closing)
            : null;

        return SpecializedReportCursorCodec.Encode(CursorKind, new Payload(
            cursor.AfterPeriodUtc,
            cursor.AfterEntryId,
            cursor.RunningBalance,
            cursor.SnapshotId,
            totals));
    }

    public static AccountCardReportCursor Decode(string value)
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

        return new AccountCardReportCursor
        {
            AfterPeriodUtc = payload.AfterPeriodUtc.Kind == DateTimeKind.Local
                ? payload.AfterPeriodUtc.ToUniversalTime()
                : DateTime.SpecifyKind(payload.AfterPeriodUtc, DateTimeKind.Utc),
            AfterEntryId = payload.AfterEntryId,
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
        [property: JsonRequired] long AfterEntryId,
        [property: JsonRequired] decimal RunningBalance,
        [property: JsonRequired] Guid SnapshotId,
        [property: JsonRequired] Totals? Totals);
}
