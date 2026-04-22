using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;

namespace NGB.Runtime.OperationalRegisters.Projections;

/// <summary>
/// Context passed to a module-provided projector for rebuilding register projections for a month.
///
/// Notes:
/// - A transaction is guaranteed to be active while the projector is called.
/// - Use <see cref="Movements"/> to read source movements for the month.
/// - Use <see cref="UnitOfWork"/> for provider-specific writes (Dapper/SQL) to derived tables.
/// </summary>
public sealed record OperationalRegisterMonthProjectionContext(
    Guid RegisterId,
    string RegisterCode,
    string RegisterCodeNorm,
    DateOnly PeriodMonth,
    DateTime NowUtc,
    IOperationalRegisterMovementsReader Movements,
    IUnitOfWork UnitOfWork);
