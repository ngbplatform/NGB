using NGB.PropertyManagement.Contracts.Receivables;

namespace NGB.PropertyManagement.Runtime.Receivables;

public interface IReceivablesFifoApplyExecuteService
{
    Task<ReceivablesFifoApplyExecuteResponse> ExecuteAsync(
        ReceivablesFifoApplyExecuteRequest request,
        CancellationToken ct = default);
}
