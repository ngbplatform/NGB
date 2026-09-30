using NGB.PropertyManagement.Contracts.Payables;

namespace NGB.PropertyManagement.Runtime.Payables;

public interface IPayablesOpenItemsDetailsService
{
    Task<PayablesOpenItemsDetailsResponse> GetOpenItemsDetailsAsync(
        Guid partyId,
        Guid propertyId,
        DateOnly? asOfMonth = null,
        DateOnly? toMonth = null,
        CancellationToken ct = default);

    Task<PayablesOpenItemsDetailsResponse> GetOpenItemsDetailsPageAsync(
        Guid partyId,
        Guid propertyId,
        DateOnly? asOfMonth,
        DateOnly? toMonth,
        int chargeOffset,
        int creditOffset,
        int allocationOffset,
        int limit,
        CancellationToken ct = default);
}
