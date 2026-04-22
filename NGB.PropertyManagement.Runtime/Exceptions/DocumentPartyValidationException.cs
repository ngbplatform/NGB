using NGB.Tools.Exceptions;

namespace NGB.PropertyManagement.Runtime.Exceptions;

public sealed class DocumentPartyValidationException(
    string message,
    string errorCode,
    IReadOnlyDictionary<string, object?>? context = null)
    : NgbValidationException(message, errorCode, context)
{
    public static DocumentPartyValidationException NotFound(string documentType, Guid partyId, string field)
        => Create(documentType, partyId, field, null, "Selected party was not found.", $"{documentType}.party.not_found");

    public static DocumentPartyValidationException Deleted(string documentType, Guid partyId, string field)
        => Create(documentType, partyId, field, null, "Selected party is marked for deletion.", $"{documentType}.party.deleted");

    public static DocumentPartyValidationException MustBeTenant(string documentType, Guid partyId, string field)
        => Create(documentType, partyId, field, "Tenant", "Selected party must be marked as Tenant.", $"{documentType}.party.must_be_tenant");

    public static DocumentPartyValidationException MustBeVendor(string documentType, Guid partyId, string field)
        => Create(documentType, partyId, field, "Vendor", "Selected party must be marked as Vendor.", $"{documentType}.party.must_be_vendor");

    private static DocumentPartyValidationException Create(
        string documentType,
        Guid partyId,
        string field,
        string? requiredRole,
        string message,
        string errorCode)
        => new(
            message,
            errorCode,
            BuildContext(documentType, partyId, field, requiredRole, message));

    private static IReadOnlyDictionary<string, object?> BuildContext(
        string documentType,
        Guid partyId,
        string field,
        string? requiredRole,
        string message)
    {
        var ctx = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["documentType"] = documentType,
            ["partyId"] = partyId,
            ["field"] = field,
            ["errors"] = new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [field] = [message]
            }
        };

        if (!string.IsNullOrWhiteSpace(requiredRole))
            ctx["requiredRole"] = requiredRole;

        return ctx;
    }
}
