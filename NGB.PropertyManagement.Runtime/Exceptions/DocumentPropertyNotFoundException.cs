using NGB.Tools.Exceptions;

namespace NGB.PropertyManagement.Runtime.Exceptions;

public sealed class DocumentPropertyNotFoundException(string documentType, Guid propertyId)
    : NgbValidationException(
        message: "Selected property was not found.",
        errorCode: $"{documentType}.property.not_found",
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
                ["property_id"] = ["Selected property was not found."]
            }
        };
}
