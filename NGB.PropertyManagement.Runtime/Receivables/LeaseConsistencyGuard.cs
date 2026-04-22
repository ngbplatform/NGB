using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Exceptions;

namespace NGB.PropertyManagement.Runtime.Receivables;

/// <summary>
/// Guards that pm.receivable_* documents reference a lease consistent with (party_id, property_id).
///
/// The lease head is treated as a source of truth.
/// </summary>
internal static class LeaseConsistencyGuard
{
    public static async Task EnsureAsync(
        Guid documentId,
        Guid leaseId,
        Guid partyId,
        Guid propertyId,
        IPropertyManagementDocumentReaders readers,
        CancellationToken ct)
    {
        PmLeaseHead lease;
        try
        {
            lease = await readers.ReadLeaseHeadAsync(leaseId, ct);
        }
        catch (InvalidOperationException)
        {
            throw ReceivableLeaseConsistencyValidationException.LeaseNotFound(documentId, leaseId);
        }

        if (lease.PrimaryPartyId != partyId)
        {
            throw ReceivableLeaseConsistencyValidationException.PartyMismatch(
                documentId: documentId,
                leaseId: leaseId,
                expectedPartyId: lease.PrimaryPartyId,
                actualPartyId: partyId);
        }

        if (lease.PropertyId != propertyId)
        {
            throw ReceivableLeaseConsistencyValidationException.PropertyMismatch(
                documentId: documentId,
                leaseId: leaseId,
                expectedPropertyId: lease.PropertyId,
                actualPropertyId: propertyId);
        }
    }
}
