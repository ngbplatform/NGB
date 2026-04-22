using System.Text.Json.Serialization;

namespace NGB.PropertyManagement.Contracts.Receivables;

/// <summary>
/// Reconciliation report request.
///
/// Semantics by mode:
/// - Movement mode computes net changes within the month range [FromMonthInclusive..ToMonthInclusive] (inclusive).
///   - AR side = SUM(debit - credit) on accounting_turnovers for the AR account in the requested range.
///   - Open-items side = SUM(amount) on opreg movements in the requested range (with storno rows treated as sign inversions).
/// - Balance mode computes an as-of / month-end reconciliation at <see cref="ToMonthInclusive"/>.
///   - AR side = latest closed balance (if any) rolled forward by turnovers through ToMonthInclusive.
///   - Open-items side = cumulative open-items movements through ToMonthInclusive.
///
/// Notes:
/// - Results are grouped by (party, property, lease) by projecting those three dimensions from DimensionSetId.
/// - <see cref="FromMonthInclusive"/> is retained for a stable request shape shared with movement mode; balance mode is defined by the cutoff month (<see cref="ToMonthInclusive"/>).
/// </summary>
public sealed record ReceivablesReconciliationRequest(
    DateOnly FromMonthInclusive,
    DateOnly ToMonthInclusive,
    ReceivablesReconciliationMode Mode = ReceivablesReconciliationMode.Movement);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReceivablesReconciliationMode
{
    Movement = 1,
    Balance = 2,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReceivablesReconciliationRowKind
{
    Matched = 1,
    Mismatch = 2,
    GlOnly = 3,
    OpenItemsOnly = 4,
}

public sealed record ReceivablesReconciliationReport(
    DateOnly FromMonthInclusive,
    DateOnly ToMonthInclusive,
    ReceivablesReconciliationMode Mode,
    Guid ArAccountId,
    Guid OpenItemsRegisterId,
    decimal TotalArNet,
    decimal TotalOpenItemsNet,
    decimal TotalDiff,
    int RowCount,
    int MismatchRowCount,
    IReadOnlyList<ReceivablesReconciliationRow> Rows);

public sealed record ReceivablesReconciliationRow(
    Guid PartyId,
    string? PartyDisplay,
    Guid PropertyId,
    string? PropertyDisplay,
    Guid LeaseId,
    string? LeaseDisplay,
    decimal ArNet,
    decimal OpenItemsNet,
    decimal Diff,
    ReceivablesReconciliationRowKind RowKind,
    bool HasDiff);
