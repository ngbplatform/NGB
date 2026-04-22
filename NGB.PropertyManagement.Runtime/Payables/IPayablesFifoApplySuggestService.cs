using NGB.PropertyManagement.Contracts.Payables;

namespace NGB.PropertyManagement.Runtime.Payables;

public interface IPayablesFifoApplySuggestService
{
    Task<PayablesSuggestFifoApplyResponse> SuggestAsync(
        PayablesSuggestFifoApplyRequest request,
        CancellationToken ct = default);
}
