using NGB.Tools.Exceptions;

namespace NGB.PropertyManagement.Runtime.Exceptions;

public sealed class MaintenanceRequestValidationException(
    string message,
    string errorCode,
    IReadOnlyDictionary<string, object?>? context = null)
    : NgbValidationException(message, errorCode, context)
{
    public static MaintenanceRequestValidationException PropertyNotFound(Guid propertyId, Guid? documentId = null)
        => new(
            message: "Selected property was not found.",
            errorCode: "pm.maintenance_request.property.not_found",
            context: BuildContext(documentId, propertyId: propertyId, errors: new Dictionary<string, string[]>
            {
                ["property_id"] = ["Selected property was not found."]
            }));

    public static MaintenanceRequestValidationException PropertyDeleted(Guid propertyId, Guid? documentId = null)
        => new(
            message: "Selected property is marked for deletion.",
            errorCode: "pm.maintenance_request.property.deleted",
            context: BuildContext(documentId, propertyId: propertyId, errors: new Dictionary<string, string[]>
            {
                ["property_id"] = ["Selected property is marked for deletion."]
            }));

    public static MaintenanceRequestValidationException PartyNotFound(Guid partyId, Guid? documentId = null)
        => new(
            message: "Selected party was not found.",
            errorCode: "pm.maintenance_request.party.not_found",
            context: BuildContext(documentId, partyId: partyId, errors: new Dictionary<string, string[]>
            {
                ["party_id"] = ["Selected party was not found."]
            }));

    public static MaintenanceRequestValidationException PartyDeleted(Guid partyId, Guid? documentId = null)
        => new(
            message: "Selected party is marked for deletion.",
            errorCode: "pm.maintenance_request.party.deleted",
            context: BuildContext(documentId, partyId: partyId, errors: new Dictionary<string, string[]>
            {
                ["party_id"] = ["Selected party is marked for deletion."]
            }));

    public static MaintenanceRequestValidationException CategoryNotFound(Guid categoryId, Guid? documentId = null)
        => new(
            message: "Selected maintenance category was not found.",
            errorCode: "pm.maintenance_request.category.not_found",
            context: BuildContext(documentId, categoryId: categoryId, errors: new Dictionary<string, string[]>
            {
                ["category_id"] = ["Selected maintenance category was not found."]
            }));

    public static MaintenanceRequestValidationException CategoryDeleted(Guid categoryId, Guid? documentId = null)
        => new(
            message: "Selected maintenance category is marked for deletion.",
            errorCode: "pm.maintenance_request.category.deleted",
            context: BuildContext(documentId, categoryId: categoryId, errors: new Dictionary<string, string[]>
            {
                ["category_id"] = ["Selected maintenance category is marked for deletion."]
            }));

    public static MaintenanceRequestValidationException SubjectRequired(Guid? documentId = null)
        => new(
            message: "Subject is required.",
            errorCode: "pm.maintenance_request.subject.required",
            context: BuildContext(documentId, errors: new Dictionary<string, string[]>
            {
                ["subject"] = ["Subject is required."]
            }));

    public static MaintenanceRequestValidationException PriorityInvalid(string? priority, Guid? documentId = null)
        => new(
            message: "Priority must be one of: Emergency, High, Normal, Low.",
            errorCode: "pm.maintenance_request.priority.invalid",
            context: BuildContext(documentId, priority: priority, errors: new Dictionary<string, string[]>
            {
                ["priority"] = ["Priority must be one of: Emergency, High, Normal, Low."]
            }));

    private static IReadOnlyDictionary<string, object?> BuildContext(
        Guid? documentId = null,
        Guid? propertyId = null,
        Guid? partyId = null,
        Guid? categoryId = null,
        string? priority = null,
        IReadOnlyDictionary<string, string[]>? errors = null)
    {
        var ctx = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (documentId is not null)
            ctx["documentId"] = documentId.Value;
        if (propertyId is not null)
            ctx["propertyId"] = propertyId.Value;
        if (partyId is not null)
            ctx["partyId"] = partyId.Value;
        if (categoryId is not null)
            ctx["categoryId"] = categoryId.Value;
        if (priority is not null)
            ctx["priority"] = priority;
        if (errors is not null)
            ctx["errors"] = errors;
        return ctx;
    }
}
