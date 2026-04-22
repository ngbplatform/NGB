using NGB.Tools.Exceptions;

namespace NGB.PropertyManagement.Runtime.Exceptions;

/// <summary>
/// Domain validation for lease-scoped receivables documents posting.
///
/// Ensures referenced lease is consistent with the document (party_id, property_id).
/// This is a client-actionable validation error (HTTP 400 via GlobalErrorHandling).
/// </summary>
public sealed class ReceivableLeaseConsistencyValidationException(
    string message,
    string errorCode,
    IReadOnlyDictionary<string, object?>? context = null)
    : NgbValidationException(message, errorCode, context)
{
    public static ReceivableLeaseConsistencyValidationException LeaseNotFound(Guid documentId, Guid leaseId)
        => new(
            "Selected lease was not found.",
            errorCode: "pm.validation.receivables.lease_not_found",
            context: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["documentId"] = documentId,
                ["leaseId"] = leaseId,
                ["errors"] = new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["lease_id"] = ["Selected lease was not found."]
                }
            });

    public static ReceivableLeaseConsistencyValidationException PartyMismatch(
        Guid documentId,
        Guid leaseId,
        Guid expectedPartyId,
        Guid actualPartyId)
        => new(
            "Selected tenant does not match the lease.",
            errorCode: "pm.validation.receivables.lease_party_mismatch",
            context: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["documentId"] = documentId,
                ["leaseId"] = leaseId,
                ["expectedPartyId"] = expectedPartyId,
                ["actualPartyId"] = actualPartyId,
                ["errors"] = new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["party_id"] = ["Tenant must match the lease."],
                    ["lease_id"] = ["The selected lease belongs to a different tenant."]
                }
            });

    public static ReceivableLeaseConsistencyValidationException PropertyMismatch(
        Guid documentId,
        Guid leaseId,
        Guid expectedPropertyId,
        Guid actualPropertyId)
        => new(
            "Selected property does not match the lease.",
            errorCode: "pm.validation.receivables.lease_property_mismatch",
            context: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["documentId"] = documentId,
                ["leaseId"] = leaseId,
                ["expectedPropertyId"] = expectedPropertyId,
                ["actualPropertyId"] = actualPropertyId,
                ["errors"] = new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["property_id"] = ["Property must match the lease."],
                    ["lease_id"] = ["The selected lease belongs to a different property."]
                }
            });
}
