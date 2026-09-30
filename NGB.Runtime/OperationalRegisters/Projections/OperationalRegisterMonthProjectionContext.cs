using NGB.Persistence.OperationalRegisters;

namespace NGB.Runtime.OperationalRegisters.Projections;

/// <summary>
/// Context passed to a module-provided projector for rebuilding register projections for a month.
///
/// Notes:
/// - A transaction is guaranteed to be active while the projector is called.
/// - Use <see cref="Movements"/> to read source movements for the month.
/// - Write projections through provider-neutral persistence ports injected into the projector.
/// </summary>
public sealed record OperationalRegisterMonthProjectionContext(
    Guid RegisterId,
    string RegisterCode,
    string RegisterCodeNorm,
    DateOnly PeriodMonth,
    DateTime NowUtc,
    IOperationalRegisterMovementsReader Movements);
