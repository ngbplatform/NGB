using NGB.Core.Documents;
using NGB.Definitions.Documents.Validation;
using NGB.Persistence.Locks;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Exceptions;

namespace NGB.PropertyManagement.Runtime.Documents.Validation;

/// <summary>
/// PM business invariant:
/// For the same property, Posted leases must not overlap by date range.
///
/// Requirement:
/// - Validate ONLY on Post (drafts may overlap).
/// - The operation must be concurrency-safe.
///
/// Concurrency:
/// - We take an advisory lock on property_id (catalog id) inside the posting transaction.
///   This prevents two concurrent Post operations from racing.
/// </summary>
public sealed class LeaseOverlapPostValidator(
    IPropertyManagementDocumentReaders readers,
    IAdvisoryLockManager advisoryLocks)
    : IDocumentPostValidator
{
    public string TypeCode => PropertyManagementCodes.Lease;

    public async Task ValidateBeforePostAsync(DocumentRecord documentForUpdate, CancellationToken ct)
    {
        DocumentValidatorBindingGuard.EnsureExpectedType(documentForUpdate, TypeCode, nameof(LeaseOverlapPostValidator));

        var lease = await readers.ReadLeaseHeadAsync(documentForUpdate.Id, ct);
        var propertyId = lease.PropertyId;
        var startOnUtc = lease.StartOnUtc;
        var endOnUtc = lease.EndOnUtc;

        var property = await readers.ReadPropertyHeadAsync(propertyId, ct);
        if (property is null)
            throw new LeasePropertyNotFoundException(propertyId);

        if (property.IsDeleted)
            throw new LeasePropertyDeletedException(propertyId);

        if (!string.Equals(property.Kind, "Unit", StringComparison.OrdinalIgnoreCase))
            throw new LeasePropertyMustBeUnitException(propertyId, property.Kind);

        // Serialize posting per property.
        await advisoryLocks.LockCatalogAsync(propertyId, ct);

        var conflict = await readers.FindFirstOverlappingPostedLeaseAsync(
            currentLeaseId: documentForUpdate.Id,
            propertyId: propertyId,
            thisStartOnUtc: startOnUtc,
            thisEndOnUtc: endOnUtc,
            ct);

        if (conflict is null)
            return;

        throw new LeaseOverlapsAnotherPostedLeaseException(
            propertyId,
            conflict.LeaseId,
            startOnUtc,
            endOnUtc,
            conflict.StartOnUtc,
            conflict.EndOnUtc);
    }
}
