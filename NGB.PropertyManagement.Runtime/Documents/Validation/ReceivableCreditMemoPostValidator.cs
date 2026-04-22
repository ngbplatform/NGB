using NGB.Core.Documents;
using NGB.Definitions.Documents.Validation;
using NGB.Persistence.Documents;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.PropertyManagement.Runtime.Policy;

namespace NGB.PropertyManagement.Runtime.Documents.Validation;

public sealed class ReceivableCreditMemoPostValidator(
    IPropertyManagementDocumentReaders readers,
    IDocumentRepository documents,
    IPropertyManagementPartyReader parties)
    : IDocumentPostValidator
{
    public string TypeCode => PropertyManagementCodes.ReceivableCreditMemo;

    public async Task ValidateBeforePostAsync(DocumentRecord documentForUpdate, CancellationToken ct)
    {
        DocumentValidatorBindingGuard.EnsureExpectedType(documentForUpdate, TypeCode, nameof(ReceivableCreditMemoPostValidator));

        var memo = await readers.ReadReceivableCreditMemoHeadAsync(documentForUpdate.Id, ct);
        var leaseDocument = await documents.GetAsync(memo.LeaseId, ct);

        if (leaseDocument is null
            || !string.Equals(leaseDocument.TypeCode, PropertyManagementCodes.Lease, StringComparison.OrdinalIgnoreCase))
        {
            throw ReceivableCreditMemoValidationException.LeaseNotFound(memo.LeaseId, documentForUpdate.Id);
        }

        if (leaseDocument.Status == DocumentStatus.MarkedForDeletion)
            throw ReceivableCreditMemoValidationException.LeaseMarkedForDeletion(memo.LeaseId, documentForUpdate.Id);

        if (memo.Amount <= 0m)
            throw ReceivableCreditMemoValidationException.AmountMustBePositive(memo.Amount, documentForUpdate.Id);
        
        if (memo.ChargeTypeId is null)
            throw ReceivableCreditMemoValidationException.ClassificationRequired(documentForUpdate.Id);

        try
        {
            await readers.ReadChargeTypeHeadAsync(memo.ChargeTypeId.Value, ct);
        }
        catch (InvalidOperationException)
        {
            throw ReceivableCreditMemoValidationException.ChargeTypeNotFound(memo.ChargeTypeId.Value, documentForUpdate.Id);
        }

        var lease = await readers.ReadLeaseHeadAsync(memo.LeaseId, ct);
        await PartyRoleValidationGuards.EnsureTenantPartyAsync(TypeCode, "lease_id", lease.PrimaryPartyId, parties, ct);

        var property = await readers.ReadPropertyHeadAsync(memo.PropertyId, ct);
        if (property is null)
            throw new DocumentPropertyNotFoundException(TypeCode, memo.PropertyId);

        if (property.IsDeleted)
            throw new DocumentPropertyDeletedException(TypeCode, memo.PropertyId);

        if (!string.Equals(property.Kind, "Unit", StringComparison.OrdinalIgnoreCase))
            throw new DocumentPropertyMustBeUnitException(TypeCode, memo.PropertyId, property.Kind);
    }
}
