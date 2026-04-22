using NGB.Application.Abstractions.Services;
using NGB.Core.Documents;
using NGB.Definitions.Documents.Validation;
using NGB.Persistence.Catalogs;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Exceptions;

namespace NGB.PropertyManagement.Runtime.Documents.Validation;

public sealed class PayableChargePostValidator(
    IPropertyManagementDocumentReaders readers,
    ICatalogRepository catalogRepository,
    ICatalogService catalogService)
    : IDocumentPostValidator
{
    public string TypeCode => PropertyManagementCodes.PayableCharge;

    public async Task ValidateBeforePostAsync(DocumentRecord documentForUpdate, CancellationToken ct)
    {
        DocumentValidatorBindingGuard.EnsureExpectedType(documentForUpdate, TypeCode, nameof(PayableChargePostValidator));

        var charge = await readers.ReadPayableChargeHeadAsync(documentForUpdate.Id, ct);

        if (charge.Amount <= 0m)
            throw PayableChargeValidationException.AmountMustBePositive(charge.Amount, documentForUpdate.Id);

        await PayableChargeValidationGuards.ValidateVendorAsync(charge.PartyId, documentForUpdate.Id, catalogRepository, catalogService, ct);
        await PayableChargeValidationGuards.ValidatePropertyAsync(charge.PropertyId, documentForUpdate.Id, readers, ct);
        await PayableChargeValidationGuards.ValidateChargeTypeAsync(charge.ChargeTypeId, documentForUpdate.Id, catalogRepository, ct);

        try
        {
            await readers.ReadPayableChargeTypeHeadAsync(charge.ChargeTypeId, ct);
        }
        catch (InvalidOperationException)
        {
            throw PayableChargeValidationException.ChargeTypeNotFound(charge.ChargeTypeId, documentForUpdate.Id);
        }
    }
}
