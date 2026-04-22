using System.Text.Json;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Services;
using NGB.Core.Catalogs.Exceptions;
using NGB.Tools.Exceptions;

namespace NGB.PropertyManagement.Runtime.Policy;

public sealed class PropertyManagementPartyReader(ICatalogService catalogs) : IPropertyManagementPartyReader
{
    public async Task<PropertyManagementParty?> TryGetAsync(Guid partyId, CancellationToken ct = default)
    {
        try
        {
            var item = await catalogs.GetByIdAsync(PropertyManagementCodes.Party, partyId, ct);
            return ToModel(item);
        }
        catch (CatalogNotFoundException)
        {
            return null;
        }
    }

    public async Task<PropertyManagementParty> GetRequiredAsync(Guid partyId, CancellationToken ct = default)
        => await TryGetAsync(partyId, ct)
           ?? throw new NgbConfigurationViolationException(
               $"Party '{partyId}' was not found.",
               context: new Dictionary<string, object?>
               {
                   ["catalogType"] = PropertyManagementCodes.Party,
                   ["partyId"] = partyId
               });

    private static PropertyManagementParty ToModel(CatalogItemDto item)
        => new(
            PartyId: item.Id,
            Display: item.Display,
            IsTenant: GetBool(item, "is_tenant"),
            IsVendor: GetBool(item, "is_vendor"),
            IsDeleted: item.IsDeleted);

    private static bool GetBool(CatalogItemDto item, string field)
    {
        var fields = item.Payload.Fields;
        if (fields is null || !fields.TryGetValue(field, out var el))
            throw new NgbConfigurationViolationException($"Party field '{field}' is missing.");

        try
        {
            return el.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => bool.Parse(el.GetString() ?? string.Empty),
                _ => throw new NgbConfigurationViolationException($"Unexpected JSON value kind '{el.ValueKind}' for '{field}'.")
            };
        }
        catch (Exception ex)
        {
            throw new NgbConfigurationViolationException(
                $"Party field '{field}' is not a valid boolean.",
                context: new Dictionary<string, object?>
                {
                    ["catalogType"] = PropertyManagementCodes.Party,
                    ["partyId"] = item.Id,
                    ["field"] = field,
                    ["valueKind"] = el.ValueKind.ToString(),
                    ["error"] = ex.Message
                });
        }
    }
}
