using System.Text.Json.Serialization;

namespace NGB.PropertyManagement.Contracts.Payables;

/// <summary>
/// Reconciliation report request for payables.
///
/// Semantics by mode:
/// - Movement mode computes net changes within the month range [FromMonthInclusive..ToMonthInclusive] (inclusive).
///   - AP side = SUM(credit - debit) on accounting_turnovers for the AP account in the requested range.
///   - Open-items side = SUM(amount) on payables open-items movements in the requested range (with storno rows treated as sign inversions).
/// - Balance mode computes an as-of / month-end reconciliation at <see cref="ToMonthInclusive"/>.
///   - AP side = latest closed balance (if any) rolled forward by turnovers through ToMonthInclusive, normalized into liability-positive orientation.
///   - Open-items side = cumulative payables open-items movements through ToMonthInclusive.
///
/// Notes:
/// - Results are grouped by (vendor, property) by projecting those dimensions from DimensionSetId.
/// - <see cref="FromMonthInclusive"/> is retained for a stable request shape shared with movement mode; balance mode is defined by the cutoff month (<see cref="ToMonthInclusive"/>).
/// </summary>
public sealed record PayablesReconciliationRequest(
    DateOnly FromMonthInclusive,
    DateOnly ToMonthInclusive,
    PayablesReconciliationMode Mode = PayablesReconciliationMode.Movement);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PayablesReconciliationMode
{
    Movement = 1,
    Balance = 2,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PayablesReconciliationRowKind
{
    Matched = 1,
    Mismatch = 2,
    GlOnly = 3,
    OpenItemsOnly = 4,
}

public sealed record PayablesReconciliationReport(
    DateOnly FromMonthInclusive,
    DateOnly ToMonthInclusive,
    PayablesReconciliationMode Mode,
    Guid ApAccountId,
    Guid OpenItemsRegisterId,
    decimal TotalApNet,
    decimal TotalOpenItemsNet,
    decimal TotalDiff,
    int RowCount,
    int MismatchRowCount,
    IReadOnlyList<PayablesReconciliationRow> Rows);

public sealed record PayablesReconciliationRow(
    Guid VendorId,
    string? VendorDisplay,
    Guid PropertyId,
    string? PropertyDisplay,
    decimal ApNet,
    decimal OpenItemsNet,
    decimal Diff,
    PayablesReconciliationRowKind RowKind,
    bool HasDiff);
