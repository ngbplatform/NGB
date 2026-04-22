using NGB.PropertyManagement.Contracts.Receivables;

namespace NGB.PropertyManagement.Runtime.Receivables;

public interface IReceivablesApplyBatchService
{
    Task<ReceivablesApplyBatchResponse> ExecuteAsync(
        ReceivablesApplyBatchRequest request,
        CancellationToken ct = default);
}
