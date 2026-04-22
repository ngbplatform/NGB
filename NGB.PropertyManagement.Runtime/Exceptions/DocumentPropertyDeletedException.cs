using NGB.Tools.Exceptions;

namespace NGB.PropertyManagement.Runtime.Exceptions;

public sealed class DocumentPropertyDeletedException(string documentType, Guid propertyId)
    : NgbValidationException(
        message: "Selected property is marked for deletion.",
        errorCode: $"{documentType}.property.deleted",
        context: BuildContext(documentType, propertyId))
{
    private static IReadOnlyDictionary<string, object?> BuildContext(string documentType, Guid propertyId)
        => new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["documentType"] = documentType,
            ["propertyId"] = propertyId,
            ["field"] = "property_id",
            ["errors"] = new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["property_id"] = ["Selected property is marked for deletion."]
            }
        };
}
