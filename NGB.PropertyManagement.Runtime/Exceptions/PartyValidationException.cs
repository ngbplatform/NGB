using NGB.Tools.Exceptions;

namespace NGB.PropertyManagement.Runtime.Exceptions;

public sealed class PartyValidationException(
    string message,
    string errorCode,
    IReadOnlyDictionary<string, object?>? context = null)
    : NgbValidationException(message, errorCode, context)
{
    public static PartyValidationException AtLeastOneRoleRequired(
        Guid? catalogId = null,
        bool? isTenant = null,
        bool? isVendor = null)
    {
        const string message = "Select at least one role: Tenant or Vendor.";
        return new(
            message,
            "pm.validation.party.role_required",
            BuildContext(
                catalogId,
                isTenant,
                isVendor,
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["is_tenant"] = [message],
                    ["is_vendor"] = [message]
                }));
    }

    private static IReadOnlyDictionary<string, object?> BuildContext(
        Guid? catalogId,
        bool? isTenant,
        bool? isVendor,
        IReadOnlyDictionary<string, string[]> errors)
    {
        var ctx = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["errors"] = errors
        };

        if (catalogId is not null)
            ctx["catalogId"] = catalogId.Value;
        if (isTenant is not null)
            ctx["isTenant"] = isTenant.Value;
        if (isVendor is not null)
            ctx["isVendor"] = isVendor.Value;

        return ctx;
    }
}
