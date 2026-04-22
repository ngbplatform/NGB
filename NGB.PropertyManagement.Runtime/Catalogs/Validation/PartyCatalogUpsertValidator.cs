using System.Text.Json;
using NGB.Definitions.Catalogs.Validation;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.Tools.Exceptions;

namespace NGB.PropertyManagement.Runtime.Catalogs.Validation;

/// <summary>
/// Runtime validation for pm.party roles.
///
/// Semantics:
/// - a party must have at least one active role: Tenant and/or Vendor;
/// - for backward compatibility, create defaults are Tenant=true, Vendor=false
///   when both role flags are omitted from the payload.
///
/// This keeps existing Receivables/Lease flows green while making the new role model explicit.
/// Future Payables docs can safely rely on is_vendor.
/// </summary>
public sealed class PartyCatalogUpsertValidator : ICatalogUpsertValidator
{
    public string TypeCode => PropertyManagementCodes.Party;

    public Task ValidateUpsertAsync(CatalogUpsertValidationContext context, CancellationToken ct)
    {
        if (!string.Equals(context.TypeCode, TypeCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new NgbConfigurationViolationException(
                $"{nameof(PartyCatalogUpsertValidator)} is configured for '{TypeCode}', not '{context.TypeCode}'.");
        }

        var hasTenant = TryReadBool(context.Fields, "is_tenant", out var isTenant);
        var hasVendor = TryReadBool(context.Fields, "is_vendor", out var isVendor);

        if (!hasTenant && !hasVendor)
        {
            isTenant = true;
            isVendor = false;
        }
        else
        {
            isTenant ??= true;
            isVendor ??= false;
        }

        if (isTenant is not true && isVendor is not true)
            throw PartyValidationException.AtLeastOneRoleRequired(context.CatalogId, isTenant, isVendor);

        return Task.CompletedTask;
    }

    private static bool TryReadBool(IReadOnlyDictionary<string, object?> fields, string key, out bool? value)
    {
        value = null;
        if (!fields.TryGetValue(key, out var raw) || raw is null)
            return false;

        switch (raw)
        {
            case bool b:
                value = b;
                return true;
            case string s when bool.TryParse(s, out var parsed):
                value = parsed;
                return true;
            case JsonElement { ValueKind: JsonValueKind.True }:
                value = true;
                return true;
            case JsonElement { ValueKind: JsonValueKind.False }:
                value = false;
                return true;
            case JsonElement { ValueKind: JsonValueKind.String } el when bool.TryParse(el.GetString(), out var parsed):
                value = parsed;
                return true;
            case JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined }:
                return true;
            default:
                return false;
        }
    }
}
