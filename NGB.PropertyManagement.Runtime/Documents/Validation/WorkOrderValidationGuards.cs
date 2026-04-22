using NGB.Core.Documents;
using NGB.Persistence.Catalogs;
using NGB.Persistence.Documents;
using NGB.PropertyManagement.Runtime.Exceptions;

namespace NGB.PropertyManagement.Runtime.Documents.Validation;

internal static class WorkOrderValidationGuards
{
    public static async Task ValidateRequestAsync(
        Guid requestId,
        Guid? documentId,
        IDocumentRepository documents,
        CancellationToken ct)
    {
        var request = await documents.GetAsync(requestId, ct);
        if (request is null
            || !string.Equals(request.TypeCode, PropertyManagementCodes.MaintenanceRequest,
                StringComparison.OrdinalIgnoreCase))
        {
            throw WorkOrderValidationException.RequestNotFound(requestId, documentId);
        }

        if (request.Status != DocumentStatus.Posted)
            throw WorkOrderValidationException.RequestMustBePosted(requestId, documentId, request.Status);
    }

    public static async Task ValidateAssignedPartyAsync(
        Guid assignedPartyId,
        Guid? documentId,
        ICatalogRepository catalogRepository,
        CancellationToken ct)
    {
        var party = await catalogRepository.GetAsync(assignedPartyId, ct);
        if (party is null
            || !string.Equals(party.CatalogCode, PropertyManagementCodes.Party, StringComparison.OrdinalIgnoreCase))
        {
            throw WorkOrderValidationException.AssignedPartyNotFound(assignedPartyId, documentId);
        }

        if (party.IsDeleted)
            throw WorkOrderValidationException.AssignedPartyDeleted(assignedPartyId, documentId);
    }

    public static string NormalizeCostResponsibilityOrThrow(string? value, Guid? documentId = null)
    {
        var normalized = NormalizeCostResponsibility(value);
        if (normalized is null)
            throw WorkOrderValidationException.CostResponsibilityInvalid(value, documentId);

        return normalized;
    }

    public static string? NormalizeCostResponsibility(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return null;

        return trimmed.ToUpperInvariant() switch
        {
            "OWNER" => "Owner",
            "TENANT" => "Tenant",
            "COMPANY" => "Company",
            "UNKNOWN" => "Unknown",
            _ => null
        };
    }
}
