using NGB.Accounting.Turnovers;

namespace NGB.Persistence.Readers;

/// <summary>
/// Reads monthly turnovers aggregated from the ground-truth register table (accounting_register_main).
/// Used for maintenance tools: rebuild/repair and deep diagnostics.
/// </summary>
public interface IAccountingTurnoverAggregationReader
{
    /// <summary>
    /// Aggregates register rows for the given month into account turnovers.
    /// The <paramref name="period"/> MUST be the first day of month.
    /// </summary>
    Task<IReadOnlyList<AccountingTurnover>> GetAggregatedFromRegisterAsync(
        DateOnly period,
        CancellationToken ct = default);
}
