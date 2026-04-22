using NGB.Tools.Exceptions;

namespace NGB.PropertyManagement.Runtime.Exceptions;

public sealed class DocumentPropertyPayloadValidationException(
    string message,
    string errorCode,
    IReadOnlyDictionary<string, object?>? context = null)
    : NgbValidationException(message, errorCode, context)
{
    public static DocumentPropertyPayloadValidationException Required(string documentType)
    {
        const string message = "Property is required.";
        return new(
            message: message,
            errorCode: $"{documentType}.property.required",
            context: BuildContext(documentType, message));
    }

    public static DocumentPropertyPayloadValidationException Invalid(string documentType)
    {
        const string message = "Select a valid property.";
        return new(
            message: message,
            errorCode: $"{documentType}.property.invalid",
            context: BuildContext(documentType, message));
    }

    private static IReadOnlyDictionary<string, object?> BuildContext(string documentType, string message)
        => new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["documentType"] = documentType,
            ["field"] = "property_id",
            ["errors"] = new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["property_id"] = [message]
            }
        };
}
