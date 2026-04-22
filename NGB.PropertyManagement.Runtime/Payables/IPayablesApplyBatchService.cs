using NGB.PropertyManagement.Contracts.Payables;

namespace NGB.PropertyManagement.Runtime.Payables;

public interface IPayablesApplyBatchService
{
    Task<PayablesApplyBatchResponse> ExecuteAsync(PayablesApplyBatchRequest request, CancellationToken ct = default);
}
