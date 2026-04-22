using NGB.Accounting.CashFlow;

namespace NGB.Persistence.Accounts;

/// <summary>
/// Read-side access to the system cash-flow line catalog.
/// The current product uses it for account metadata validation and report labeling.
/// </summary>
public interface ICashFlowLineRepository
{
    Task<IReadOnlyList<CashFlowLineDefinition>> GetAllAsync(CancellationToken ct = default);

    Task<CashFlowLineDefinition?> GetByCodeAsync(string lineCode, CancellationToken ct = default);
}
