using NGB.PropertyManagement.Contracts.Receivables;

namespace NGB.PropertyManagement.Runtime.Receivables;

public interface IReceivablesFifoApplySuggestService
{
    Task<ReceivablesFifoApplySuggestResponse> SuggestAsync(
        ReceivablesFifoApplySuggestRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// UI-oriented FIFO suggestion across all open charge and payment-credit items for a lease.
    /// </summary>
    Task<ReceivablesSuggestFifoApplyResponse> SuggestLeaseAsync(
        ReceivablesSuggestFifoApplyRequest request,
        CancellationToken ct = default);
}
