using System.Text.Json;
using NGB.Application.Abstractions.Services;
using NGB.Persistence.Catalogs;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Exceptions;

namespace NGB.PropertyManagement.Runtime.Documents.Validation;

internal static class PayableChargeValidationGuards
{
    public static async Task ValidateVendorAsync(
        Guid partyId,
        Guid? documentId,
        ICatalogRepository catalogRepository,
        ICatalogService catalogService,
        CancellationToken ct)
    {
        var party = await catalogRepository.GetAsync(partyId, ct);
        if (party is null || !string.Equals(party.CatalogCode, PropertyManagementCodes.Party, StringComparison.OrdinalIgnoreCase))
            throw PayableChargeValidationException.VendorNotFound(partyId, documentId);

        if (party.IsDeleted)
            throw PayableChargeValidationException.VendorDeleted(partyId, documentId);

        var item = await catalogService.GetByIdAsync(PropertyManagementCodes.Party, partyId, ct);
        if (!TryGetBoolean(item.Payload.Fields, "is_vendor", out var isVendor) || !isVendor)
            throw PayableChargeValidationException.VendorRoleRequired(partyId, documentId);
    }

    public static async Task ValidatePropertyAsync(
        Guid propertyId,
        Guid? documentId,
        IPropertyManagementDocumentReaders readers,
        CancellationToken ct)
    {
        var property = await readers.ReadPropertyHeadAsync(propertyId, ct);
        if (property is null)
            throw PayableChargeValidationException.PropertyNotFound(propertyId, documentId);

        if (property.IsDeleted)
            throw PayableChargeValidationException.PropertyDeleted(propertyId, documentId);
    }

    public static async Task ValidateChargeTypeAsync(
        Guid chargeTypeId,
        Guid? documentId,
        ICatalogRepository catalogRepository,
        CancellationToken ct)
    {
        var chargeType = await catalogRepository.GetAsync(chargeTypeId, ct);
        if (chargeType is null || !string.Equals(chargeType.CatalogCode, PropertyManagementCodes.PayableChargeType, StringComparison.OrdinalIgnoreCase))
            throw PayableChargeValidationException.ChargeTypeNotFound(chargeTypeId, documentId);

        if (chargeType.IsDeleted)
            throw PayableChargeValidationException.ChargeTypeDeleted(chargeTypeId, documentId);
    }

    private static bool TryGetBoolean(IReadOnlyDictionary<string, JsonElement>? fields, string key, out bool value)
    {
        value = false;
        if (fields is null || !fields.TryGetValue(key, out var el))
            return false;

        if (el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return false;

        try
        {
            if (el.ValueKind == JsonValueKind.True || el.ValueKind == JsonValueKind.False)
            {
                value = el.GetBoolean();
                return true;
            }

            if (el.ValueKind == JsonValueKind.String && bool.TryParse(el.GetString(), out var b))
            {
                value = b;
                return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }
}
