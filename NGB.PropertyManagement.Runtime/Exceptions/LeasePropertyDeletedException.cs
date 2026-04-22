using NGB.Tools.Exceptions;

namespace NGB.PropertyManagement.Runtime.Exceptions;

public sealed class LeasePropertyDeletedException(Guid propertyId)
    : NgbValidationException(
        message: "Selected property is marked for deletion.",
        errorCode: "pm.lease.property.deleted",
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
                ["property_id"] = ["Selected property is marked for deletion."]
            }
        };
}
