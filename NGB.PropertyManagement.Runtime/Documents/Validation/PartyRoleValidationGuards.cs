using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.PropertyManagement.Runtime.Policy;

namespace NGB.PropertyManagement.Runtime.Documents.Validation;

internal static class PartyRoleValidationGuards
{
    public static async Task EnsureTenantPartyAsync(
        string documentType,
        string field,
        Guid partyId,
        IPropertyManagementPartyReader parties,
        CancellationToken ct)
    {
        var party = await parties.TryGetAsync(partyId, ct);
        if (party is null)
            throw DocumentPartyValidationException.NotFound(documentType, partyId, field);

        if (party.IsDeleted)
            throw DocumentPartyValidationException.Deleted(documentType, partyId, field);

        if (!party.IsTenant)
            throw DocumentPartyValidationException.MustBeTenant(documentType, partyId, field);
    }

    public static async Task EnsureVendorPartyAsync(
        string documentType,
        string field,
        Guid partyId,
        IPropertyManagementPartyReader parties,
        CancellationToken ct)
    {
        var party = await parties.TryGetAsync(partyId, ct);
        if (party is null)
            throw DocumentPartyValidationException.NotFound(documentType, partyId, field);

        if (party.IsDeleted)
            throw DocumentPartyValidationException.Deleted(documentType, partyId, field);

        if (!party.IsVendor)
            throw DocumentPartyValidationException.MustBeVendor(documentType, partyId, field);
    }
}
