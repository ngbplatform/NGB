using NGB.Persistence.Catalogs;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.PropertyManagement.Runtime.Policy;

namespace NGB.PropertyManagement.Runtime.Documents.Validation;

internal static class MaintenanceRequestValidationGuards
{
    public static async Task ValidatePropertyAsync(
        Guid propertyId,
        Guid? documentId,
        IPropertyManagementDocumentReaders readers,
        CancellationToken ct)
    {
        var property = await readers.ReadPropertyHeadAsync(propertyId, ct);
        if (property is null)
            throw MaintenanceRequestValidationException.PropertyNotFound(propertyId, documentId);

        if (property.IsDeleted)
            throw MaintenanceRequestValidationException.PropertyDeleted(propertyId, documentId);
    }

    public static async Task ValidatePartyAsync(
        Guid partyId,
        Guid? documentId,
        IPropertyManagementPartyReader parties,
        CancellationToken ct)
    {
        var party = await parties.TryGetAsync(partyId, ct);
        if (party is null)
            throw MaintenanceRequestValidationException.PartyNotFound(partyId, documentId);

        if (party.IsDeleted)
            throw MaintenanceRequestValidationException.PartyDeleted(partyId, documentId);
    }

    public static async Task ValidateCategoryAsync(
        Guid categoryId,
        Guid? documentId,
        ICatalogRepository catalogRepository,
        CancellationToken ct)
    {
        var category = await catalogRepository.GetAsync(categoryId, ct);
        if (category is null
            || !string.Equals(category.CatalogCode, PropertyManagementCodes.MaintenanceCategory,
                StringComparison.OrdinalIgnoreCase))
        {
            throw MaintenanceRequestValidationException.CategoryNotFound(categoryId, documentId);
        }

        if (category.IsDeleted)
            throw MaintenanceRequestValidationException.CategoryDeleted(categoryId, documentId);
    }

    public static string NormalizePriorityOrThrow(string? priority, Guid? documentId = null)
    {
        var normalized = NormalizePriority(priority);
        if (normalized is null)
            throw MaintenanceRequestValidationException.PriorityInvalid(priority, documentId);

        return normalized;
    }

    public static string? NormalizePriority(string? priority)
    {
        var value = priority?.Trim();
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.ToUpperInvariant() switch
        {
            "EMERGENCY" => "Emergency",
            "HIGH" => "High",
            "NORMAL" => "Normal",
            "LOW" => "Low",
            _ => null
        };
    }
}
