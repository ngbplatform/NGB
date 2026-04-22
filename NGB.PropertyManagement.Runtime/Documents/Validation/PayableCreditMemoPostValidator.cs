using NGB.Core.Documents;
using NGB.Definitions.Documents.Validation;
using NGB.Persistence.Catalogs;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.PropertyManagement.Runtime.Policy;

namespace NGB.PropertyManagement.Runtime.Documents.Validation;

public sealed class PayableCreditMemoPostValidator(
    IPropertyManagementDocumentReaders readers,
    ICatalogRepository catalogRepository,
    IPropertyManagementPartyReader parties)
    : IDocumentPostValidator
{
    public string TypeCode => PropertyManagementCodes.PayableCreditMemo;

    public async Task ValidateBeforePostAsync(DocumentRecord documentForUpdate, CancellationToken ct)
    {
        DocumentValidatorBindingGuard.EnsureExpectedType(documentForUpdate, TypeCode, nameof(PayableCreditMemoPostValidator));

        var memo = await readers.ReadPayableCreditMemoHeadAsync(documentForUpdate.Id, ct);

        await ValidateVendorAsync(memo.PartyId, documentForUpdate.Id, ct);
        await ValidatePropertyAsync(memo.PropertyId, documentForUpdate.Id, ct);
        await ValidateChargeTypeAsync(memo.ChargeTypeId, documentForUpdate.Id, ct);

        if (memo.Amount <= 0m)
            throw PayableCreditMemoValidationException.AmountMustBePositive(memo.Amount, documentForUpdate.Id);
    }

    private async Task ValidateVendorAsync(Guid partyId, Guid? documentId, CancellationToken ct)
    {
        var party = await parties.TryGetAsync(partyId, ct);
        if (party is null)
            throw PayableCreditMemoValidationException.VendorNotFound(partyId, documentId);

        if (party.IsDeleted)
            throw PayableCreditMemoValidationException.VendorDeleted(partyId, documentId);

        if (!party.IsVendor)
            throw PayableCreditMemoValidationException.VendorRoleRequired(partyId, documentId);
    }

    private async Task ValidatePropertyAsync(Guid propertyId, Guid? documentId, CancellationToken ct)
    {
        var property = await readers.ReadPropertyHeadAsync(propertyId, ct);
        if (property is null)
            throw PayableCreditMemoValidationException.PropertyNotFound(propertyId, documentId);

        if (property.IsDeleted)
            throw PayableCreditMemoValidationException.PropertyDeleted(propertyId, documentId);
    }

    private async Task ValidateChargeTypeAsync(Guid chargeTypeId, Guid? documentId, CancellationToken ct)
    {
        var chargeType = await catalogRepository.GetAsync(chargeTypeId, ct);
        if (chargeType is null
            || !string.Equals(chargeType.CatalogCode, PropertyManagementCodes.PayableChargeType,
                StringComparison.OrdinalIgnoreCase))
        {
            throw PayableCreditMemoValidationException.ChargeTypeNotFound(chargeTypeId, documentId);
        }

        if (chargeType.IsDeleted)
            throw PayableCreditMemoValidationException.ChargeTypeDeleted(chargeTypeId, documentId);
    }
}
