using NGB.Tools.Exceptions;

namespace NGB.PropertyManagement.Runtime.Exceptions;

public sealed class LeasePrimaryPartyPayloadValidationException(
    string message,
    IReadOnlyDictionary<string, object?>? context = null)
    : NgbValidationException(message, "pm.lease.primary_party.invalid", context)
{
    public static LeasePrimaryPartyPayloadValidationException PartMissing()
        => new(
            message: "Tenant list is required.",
            context: BuildContext(
                message: "Tenant list is required.",
                primaryCount: 0,
                rowCount: 0,
                errors: new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["parties"] = ["Tenant list is required."]
                }));

    public static LeasePrimaryPartyPayloadValidationException AtLeastOneTenantRequired()
        => new(
            message: "At least one tenant is required.",
            context: BuildContext(
                message: "At least one tenant is required.",
                primaryCount: 0,
                rowCount: 0,
                errors: new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["parties"] = ["At least one tenant is required."]
                }));

    public static LeasePrimaryPartyPayloadValidationException DuplicateTenant(int rowCount)
        => new(
            message: "The same tenant cannot be added twice.",
            context: BuildContext(
                message: "The same tenant cannot be added twice.",
                primaryCount: 0,
                rowCount: rowCount,
                errors: new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["parties[].party_id"] = ["The same tenant cannot be added twice."]
                }));

    public static LeasePrimaryPartyPayloadValidationException ExactlyOnePrimaryRequired(int primaryCount, int rowCount)
        => new(
            message: "Exactly one primary tenant is required.",
            context: BuildContext(
                message: "Exactly one primary tenant is required.",
                primaryCount: primaryCount,
                rowCount: rowCount,
                errors: new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["parties"] = ["Exactly one primary tenant is required."]
                }));

    public static LeasePrimaryPartyPayloadValidationException PrimaryRoleRequired(int rowCount)
        => new(
            message: "The primary tenant row must use role 'Primary tenant'.",
            context: BuildContext(
                message: "The primary tenant row must use role 'Primary tenant'.",
                primaryCount: 1,
                rowCount: rowCount,
                errors: new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["parties[].role"] = ["Primary tenant row must use role 'Primary tenant'."],
                    ["parties[].is_primary"] = ["Exactly one row marked as primary must use role 'Primary tenant'."]
                }));

    public static LeasePrimaryPartyPayloadValidationException PrimaryRoleMustBePrimary(int rowCount)
        => new(
            message: "Rows with role 'Primary tenant' must be marked as primary.",
            context: BuildContext(
                message: "Rows with role 'Primary tenant' must be marked as primary.",
                primaryCount: 1,
                rowCount: rowCount,
                errors: new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["parties[].role"] = ["Rows with role 'Primary tenant' must be marked as primary."],
                    ["parties[].is_primary"] = ["Rows with role 'Primary tenant' must be marked as primary."]
                }));

    public static LeasePrimaryPartyPayloadValidationException DuplicateOrdinal(int rowCount)
        => new(
            message: "Tenant row order must be unique.",
            context: BuildContext(
                message: "Tenant row order must be unique.",
                primaryCount: 1,
                rowCount: rowCount,
                errors: new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["parties[].ordinal"] = ["Tenant row order must be unique."]
                }));

    private static IReadOnlyDictionary<string, object?> BuildContext(
        string message,
        int primaryCount,
        int rowCount,
        IReadOnlyDictionary<string, string[]> errors)
        => new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["documentType"] = "pm.lease",
            ["part"] = "parties",
            ["expectedPrimaryCount"] = 1,
            ["actualPrimaryCount"] = primaryCount,
            ["rowCount"] = rowCount,
            ["reason"] = message,
            ["errors"] = errors
        };
}
