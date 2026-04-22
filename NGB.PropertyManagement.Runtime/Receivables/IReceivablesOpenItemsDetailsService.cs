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
}
