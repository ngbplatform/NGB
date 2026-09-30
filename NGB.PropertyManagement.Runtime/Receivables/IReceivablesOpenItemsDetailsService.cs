using NGB.PropertyManagement.Contracts.Receivables;

namespace NGB.PropertyManagement.Runtime.Receivables;

public interface IReceivablesOpenItemsDetailsService
{
    Task<ReceivablesOpenItemsDetailsResponse> GetOpenItemsDetailsAsync(
        Guid partyId,
        Guid propertyId,
        Guid leaseId,
        DateOnly? asOfMonth = null,
        DateOnly? toMonth = null,
        CancellationToken ct = default);

    Task<ReceivablesOpenItemsDetailsResponse> GetOpenItemsDetailsPageAsync(
        Guid partyId,
        Guid propertyId,
        Guid leaseId,
        DateOnly? asOfMonth,
        DateOnly? toMonth,
        int chargeOffset,
        int creditOffset,
        int allocationOffset,
        int limit,
        CancellationToken ct = default);
}
