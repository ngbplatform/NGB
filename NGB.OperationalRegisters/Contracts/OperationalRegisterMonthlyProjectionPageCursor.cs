namespace NGB.OperationalRegisters.Contracts;

/// <summary>
/// Keyset-like cursor for projection paging.
///
/// Sort order:
/// - PeriodMonth (ascending)
/// - DimensionSetId (ascending)
///
/// Cursor points to the last emitted row.
/// </summary>
public sealed record OperationalRegisterMonthlyProjectionPageCursor(
    DateOnly AfterPeriodMonth,
    Guid AfterDimensionSetId);
