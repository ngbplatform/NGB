using NGB.Accounting.Turnovers;

namespace NGB.Persistence.Readers;

public interface IAccountingTurnoverReader
{
    Task<IReadOnlyList<AccountingTurnover>> GetForPeriodAsync(DateOnly period,  CancellationToken ct = default);
    
    Task<IReadOnlyList<AccountingTurnover>> GetRangeAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        CancellationToken ct = default);
}
