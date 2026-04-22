using NGB.Core.Documents;
using NGB.Definitions.Documents.Validation;
using NGB.Persistence.Documents;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.PropertyManagement.Runtime.Policy;

namespace NGB.PropertyManagement.Runtime.Documents.Validation;

public sealed class LateFeeChargePostValidator(
    IPropertyManagementDocumentReaders readers,
    IDocumentRepository documents,
    IPropertyManagementPartyReader parties)
    : IDocumentPostValidator
{
    public string TypeCode => PropertyManagementCodes.LateFeeCharge;

    public async Task ValidateBeforePostAsync(DocumentRecord documentForUpdate, CancellationToken ct)
    {
        DocumentValidatorBindingGuard.EnsureExpectedType(documentForUpdate, TypeCode, nameof(LateFeeChargePostValidator));

        var charge = await readers.ReadLateFeeChargeHeadAsync(documentForUpdate.Id, ct);
        var leaseDocument = await documents.GetAsync(charge.LeaseId, ct);
        if (leaseDocument is null || !string.Equals(leaseDocument.TypeCode, PropertyManagementCodes.Lease, StringComparison.OrdinalIgnoreCase))
            throw LateFeeChargeValidationException.LeaseNotFound(charge.LeaseId, documentForUpdate.Id);
        if (leaseDocument.Status == DocumentStatus.MarkedForDeletion)
            throw LateFeeChargeValidationException.LeaseMarkedForDeletion(charge.LeaseId, documentForUpdate.Id);

        if (charge.Amount <= 0m)
            throw LateFeeChargeValidationException.AmountMustBePositive(charge.Amount, documentForUpdate.Id);

        var lease = await readers.ReadLeaseHeadAsync(charge.LeaseId, ct);
        await PartyRoleValidationGuards.EnsureTenantPartyAsync(TypeCode, "lease_id", lease.PrimaryPartyId, parties, ct);

        var property = await readers.ReadPropertyHeadAsync(lease.PropertyId, ct);
        if (property is null)
            throw new DocumentPropertyNotFoundException(TypeCode, lease.PropertyId);

        if (property.IsDeleted)
            throw new DocumentPropertyDeletedException(TypeCode, lease.PropertyId);
       
        if (!string.Equals(property.Kind, "Unit", StringComparison.OrdinalIgnoreCase))
            throw new DocumentPropertyMustBeUnitException(TypeCode, lease.PropertyId, property.Kind);
    }
}
