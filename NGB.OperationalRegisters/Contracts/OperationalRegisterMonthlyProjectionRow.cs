namespace NGB.OperationalRegisters.Contracts;

/// <summary>
/// A single dimension-set row for a monthly projection (turnovers or balances) of an Operational Register.
///
/// Notes:
/// - Values are keyed by resource <c>column_code</c> (see <see cref="OperationalRegisterResource"/>).
/// - Storage for turnovers/balances is replace-based for a month: the projector recomputes the month and replaces rows.
/// </summary>
public sealed record OperationalRegisterMonthlyProjectionRow(
    Guid DimensionSetId,
    IReadOnlyDictionary<string, decimal> Values);
