using NGB.PropertyManagement.Contracts.Receivables;

namespace NGB.PropertyManagement.Runtime.Receivables;

public interface IReceivablesOpenItemsService
{
    Task<ReceivablesOpenItemsResponse> GetOpenItemsAsync(
        Guid partyId,
        Guid propertyId,
        Guid leaseId,
        CancellationToken ct = default);
}
