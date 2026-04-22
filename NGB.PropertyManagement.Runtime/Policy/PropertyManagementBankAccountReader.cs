using System.Text.Json;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Services;
using NGB.Core.Catalogs.Exceptions;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PropertyManagement.Runtime.Policy;

public sealed class PropertyManagementBankAccountReader(ICatalogService catalogs)
    : IPropertyManagementBankAccountReader
{
    public async Task<PropertyManagementBankAccount?> TryGetAsync(Guid bankAccountId, CancellationToken ct = default)
    {
        try
        {
            var item = await catalogs.GetByIdAsync(PropertyManagementCodes.BankAccount, bankAccountId, ct);
            return ToModel(item);
        }
        catch (CatalogNotFoundException)
        {
            return null;
        }
    }

    public async Task<PropertyManagementBankAccount> GetRequiredAsync(Guid bankAccountId, CancellationToken ct = default)
        => await TryGetAsync(bankAccountId, ct)
           ?? throw new NgbConfigurationViolationException(
               $"Bank account '{bankAccountId}' was not found.",
               context: new Dictionary<string, object?>
               {
                   ["catalogType"] = PropertyManagementCodes.BankAccount,
                   ["bankAccountId"] = bankAccountId
               });

    public async Task<PropertyManagementBankAccount?> TryGetDefaultAsync(CancellationToken ct = default)
    {
        var page = await catalogs.GetPageAsync(
            PropertyManagementCodes.BankAccount,
            new PageRequestDto(
                Offset: 0,
                Limit: 2,
                Search: null,
                Filters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["deleted"] = "active",
                    ["is_default"] = "true"
                }),
            ct);

        if (page.Items.Count == 0)
            return null;

        if (page.Items.Count > 1)
        {
            throw new NgbConfigurationViolationException(
                "Multiple active bank accounts are marked as default. Expected a single default bank account.",
                context: new Dictionary<string, object?>
                {
                    ["catalogType"] = PropertyManagementCodes.BankAccount,
                    ["count"] = page.Items.Count
                });
        }

        return ToModel(page.Items[0]);
    }

    private static PropertyManagementBankAccount ToModel(CatalogItemDto item)
        => new(
            BankAccountId: item.Id,
            Display: item.Display,
            GlAccountId: GetGuid(item, "gl_account_id"),
            IsDefault: GetBool(item, "is_default"),
            IsDeleted: item.IsDeleted);

    private static Guid GetGuid(CatalogItemDto item, string field)
    {
        var fields = item.Payload.Fields;
        if (fields is null || !fields.TryGetValue(field, out var el))
            throw new NgbConfigurationViolationException($"Bank account field '{field}' is missing.");

        try
        {
            return el.ParseGuidOrRef();
        }
        catch (Exception ex)
        {
            throw new NgbConfigurationViolationException(
                $"Bank account field '{field}' is not a valid GUID.",
                context: new Dictionary<string, object?>
                {
                    ["catalogType"] = PropertyManagementCodes.BankAccount,
                    ["bankAccountId"] = item.Id,
                    ["field"] = field,
                    ["valueKind"] = el.ValueKind.ToString(),
                    ["error"] = ex.Message
                });
        }
    }

    private static bool GetBool(CatalogItemDto item, string field)
    {
        var fields = item.Payload.Fields;
        if (fields is null || !fields.TryGetValue(field, out var el))
            throw new NgbConfigurationViolationException($"Bank account field '{field}' is missing.");

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
                $"Bank account field '{field}' is not a valid boolean.",
                context: new Dictionary<string, object?>
                {
                    ["catalogType"] = PropertyManagementCodes.BankAccount,
                    ["bankAccountId"] = item.Id,
                    ["field"] = field,
                    ["valueKind"] = el.ValueKind.ToString(),
                    ["error"] = ex.Message
                });
        }
    }
}
