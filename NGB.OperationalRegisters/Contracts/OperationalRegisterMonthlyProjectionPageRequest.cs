using NGB.Core.Dimensions;

namespace NGB.OperationalRegisters.Contracts;

/// <summary>
/// Page request for operational register monthly projection rows (turnovers/balances).
///
/// NOTE: current Runtime implementation performs paging in-memory by sorting rows by
/// (PeriodMonth, DimensionSetId). If this becomes a performance issue for large registers,
/// promote this to a DB keyset paging query.
/// </summary>
public sealed record OperationalRegisterMonthlyProjectionPageRequest(
    Guid RegisterId,
    DateOnly FromInclusive,
    DateOnly ToInclusive,
    IReadOnlyList<DimensionValue>? Dimensions = null,
    Guid? DimensionSetId = null,
    OperationalRegisterMonthlyProjectionPageCursor? Cursor = null,
    int PageSize = 500);
