namespace NGB.PropertyManagement.Runtime.Policy;

public interface IPropertyManagementPartyReader
{
    Task<PropertyManagementParty?> TryGetAsync(Guid partyId, CancellationToken ct = default);
    Task<PropertyManagementParty> GetRequiredAsync(Guid partyId, CancellationToken ct = default);
}

public sealed record PropertyManagementParty(
    Guid PartyId,
    string? Display,
    bool IsTenant,
    bool IsVendor,
    bool IsDeleted);
