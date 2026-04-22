using NGB.PropertyManagement.Contracts.Receivables;

namespace NGB.PropertyManagement.Runtime.Receivables;

public interface IReceivablesCustomApplyExecuteService
{
    Task<ReceivablesCustomApplyExecuteResponse> ExecuteAsync(
        ReceivablesCustomApplyExecuteRequest request,
        CancellationToken ct = default);
}
