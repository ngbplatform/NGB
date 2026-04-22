using NGB.PropertyManagement.Contracts.Receivables;

namespace NGB.PropertyManagement.Receivables;

public interface IReceivablesReconciliationService
{
    Task<ReceivablesReconciliationReport> GetAsync(
        ReceivablesReconciliationRequest request,
        CancellationToken ct = default);
}
