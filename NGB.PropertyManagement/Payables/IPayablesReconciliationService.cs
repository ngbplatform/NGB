using NGB.PropertyManagement.Contracts.Payables;

namespace NGB.PropertyManagement.Payables;

public interface IPayablesReconciliationService
{
    Task<PayablesReconciliationReport> GetAsync(PayablesReconciliationRequest request, CancellationToken ct = default);
}
