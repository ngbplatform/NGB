using NGB.Tools.Exceptions;

namespace NGB.PropertyManagement.Runtime.Exceptions;

public sealed class LeasePropertyMustBeUnitException(Guid propertyId, string? actualKind)
    : NgbValidationException(
        message: BuildMessage(actualKind),
        errorCode: "pm.lease.property.must_be_unit",
        context: BuildContext(propertyId, actualKind))
{
    private static string BuildMessage(string? actualKind)
        => string.Equals(actualKind, "Building", StringComparison.OrdinalIgnoreCase)
            ? "Select a unit, not a building."
            : "Selected property must be a unit.";

    private static IReadOnlyDictionary<string, object?> BuildContext(Guid propertyId, string? actualKind)
    {
        var message = BuildMessage(actualKind);
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["documentType"] = "pm.lease",
            ["propertyId"] = propertyId,
            ["actualKind"] = actualKind,
            ["field"] = "property_id",
            ["errors"] = new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["property_id"] = [message]
            }
        };
    }
}
