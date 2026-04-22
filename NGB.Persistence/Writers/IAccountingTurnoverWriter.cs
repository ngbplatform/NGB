using NGB.Accounting.Turnovers;

namespace NGB.Persistence.Writers;

public interface IAccountingTurnoverWriter
{
    /// <summary>
    /// Deletes all stored turnovers for the given period.
    /// 
    /// IMPORTANT: This is a maintenance operation used by rebuild tools.
    /// It must be executed within an active transaction.
    /// </summary>
    Task DeleteForPeriodAsync(DateOnly period, CancellationToken ct = default);

    Task WriteAsync(IEnumerable<AccountingTurnover> turnovers, CancellationToken ct = default);
}
