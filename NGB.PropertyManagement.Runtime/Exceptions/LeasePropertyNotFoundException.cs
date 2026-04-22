using NGB.Tools.Exceptions;

namespace NGB.PropertyManagement.Runtime.Exceptions;

public sealed class LeasePropertyNotFoundException(Guid propertyId)
    : NgbValidationException(
        message: "Selected property was not found.",
        errorCode: "pm.lease.property.not_found",
        context: BuildContext(propertyId))
{
    private static IReadOnlyDictionary<string, object?> BuildContext(Guid propertyId)
        => new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["documentType"] = "pm.lease",
            ["propertyId"] = propertyId,
            ["field"] = "property_id",
            ["errors"] = new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["property_id"] = ["Selected property was not found."]
            }
        };
}
