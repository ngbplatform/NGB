using NGB.Tools.Exceptions;

namespace NGB.PropertyManagement.Runtime.Exceptions;

/// <summary>
/// Client-actionable validation for receivables open-items query parameters.
///
/// Used by <c>/api/receivables/open-items</c> and related reports to guard against inconsistent filters.
/// </summary>
public sealed class ReceivablesOpenItemsQueryValidationException(
    string message,
    string errorCode,
    IReadOnlyDictionary<string, object?>? context = null)
    : NgbValidationException(message, errorCode, context)
{
    public static ReceivablesOpenItemsQueryValidationException PartyMismatch(
        Guid leaseId,
        Guid expectedPartyId,
        Guid actualPartyId)
        => new(
            "Selected tenant does not match the lease.",
            errorCode: "pm.validation.receivables.lease_party_mismatch",
            context: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["leaseId"] = leaseId,
                ["expectedPartyId"] = expectedPartyId,
                ["actualPartyId"] = actualPartyId,
                ["errors"] = new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["partyId"] = ["Selected tenant must match the lease."],
                    ["leaseId"] = ["The selected lease belongs to a different tenant."]
                }
            });

    public static ReceivablesOpenItemsQueryValidationException PropertyMismatch(
        Guid leaseId,
        Guid expectedPropertyId,
        Guid actualPropertyId)
        => new(
            "Selected property does not match the lease.",
            errorCode: "pm.validation.receivables.lease_property_mismatch",
            context: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["leaseId"] = leaseId,
                ["expectedPropertyId"] = expectedPropertyId,
                ["actualPropertyId"] = actualPropertyId,
                ["errors"] = new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["propertyId"] = ["Selected property must match the lease."],
                    ["leaseId"] = ["The selected lease belongs to a different property."]
                }
            });
}
