using NGB.Accounting.Accounts;
using NGB.Definitions.Catalogs.Validation;
using NGB.Metadata.Base;
using NGB.Metadata.Catalogs.Storage;
using NGB.Persistence.Catalogs.Universal;
using NGB.Persistence.Common;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.Runtime.Accounts;
using NGB.Tools.Exceptions;

namespace NGB.PropertyManagement.Runtime.Catalogs.Validation;

public sealed class BankAccountCatalogUpsertValidator(
    ICatalogTypeRegistry catalogTypes,
    ICatalogReader reader,
    IChartOfAccountsAdminService coaAdmin)
    : ICatalogUpsertValidator
{
    public string TypeCode => PropertyManagementCodes.BankAccount;

    private CatalogHeadDescriptor? _head;

    public async Task ValidateUpsertAsync(CatalogUpsertValidationContext context, CancellationToken ct)
    {
        if (!string.Equals(context.TypeCode, TypeCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new NgbConfigurationViolationException(
                $"{nameof(BankAccountCatalogUpsertValidator)} is configured for '{TypeCode}', not '{context.TypeCode}'.");
        }

        var last4 = ReadString(context.Fields, "last4");
        if (string.IsNullOrWhiteSpace(last4) || last4.Length != 4 || last4.Any(ch => !char.IsAsciiDigit(ch)))
            throw BankAccountValidationException.Last4Invalid(last4, context.CatalogId);

        var glAccountId = ReadGuid(context.Fields, "gl_account_id");
        if (glAccountId is null)
            throw BankAccountValidationException.GlAccountRequired(context.CatalogId);

        var accounts = await coaAdmin.GetAsync(includeDeleted: true, ct);
        var account = accounts.FirstOrDefault(x => x.Account.Id == glAccountId.Value);
        if (account is null)
            throw BankAccountValidationException.GlAccountNotFound(glAccountId.Value, context.CatalogId);

        if (account.IsDeleted)
            throw BankAccountValidationException.GlAccountDeleted(glAccountId.Value, context.CatalogId);

        if (!account.IsActive)
            throw BankAccountValidationException.GlAccountInactive(glAccountId.Value, context.CatalogId);

        if (account.Account.Type != AccountType.Asset)
            throw BankAccountValidationException.GlAccountMustBeAsset(glAccountId.Value, account.Account.Type, context.CatalogId);

        if (account.Account.DimensionRules.Any(x => x.IsRequired))
            throw BankAccountValidationException.GlAccountCannotRequireDimensions(glAccountId.Value, context.CatalogId);

        var isDefault = ReadBool(context.Fields, "is_default") ?? false;
        if (!isDefault)
            return;

        var query = new CatalogQuery(
            Search: null,
            Filters: new List<CatalogFilter>
            {
                new("is_default", "true")
            })
        {
            SoftDeleteFilterMode = SoftDeleteFilterMode.Active
        };

        var rows = await reader.GetPageAsync(GetHead(), query, offset: 0, limit: 5, ct);
        if (rows.Any(x => x.Id != context.CatalogId))
            throw BankAccountValidationException.MultipleActiveDefaults(context.CatalogId);
    }

    private CatalogHeadDescriptor GetHead()
    {
        if (_head is not null)
            return _head;

        var meta = catalogTypes.GetRequired(TypeCode);
        var headTable = meta.Tables.FirstOrDefault(x => x.Kind == TableKind.Head)
            ?? throw new NgbConfigurationViolationException($"Catalog '{meta.CatalogCode}' has no Head table metadata.");

        var displayColumn = meta.Presentation.DisplayColumn;
        if (string.IsNullOrWhiteSpace(displayColumn))
            throw new NgbConfigurationViolationException($"Catalog '{meta.CatalogCode}' has empty Presentation.DisplayColumn.");

        _head = new CatalogHeadDescriptor(
            CatalogCode: meta.CatalogCode,
            HeadTableName: headTable.TableName,
            DisplayColumn: displayColumn,
            Columns: headTable.Columns
                .Where(c => !string.Equals(c.ColumnName, "catalog_id", StringComparison.OrdinalIgnoreCase))
                .Select(c => new CatalogHeadColumn(c.ColumnName, c.ColumnType))
                .ToList());

        return _head;
    }

    private static string? ReadString(IReadOnlyDictionary<string, object?> fields, string key)
    {
        if (!fields.TryGetValue(key, out var raw) || raw is null)
            return null;

        return raw switch
        {
            string s => s,
            _ => raw.ToString()
        };
    }

    private static Guid? ReadGuid(IReadOnlyDictionary<string, object?> fields, string key)
    {
        if (!fields.TryGetValue(key, out var raw) || raw is null)
            return null;

        return raw switch
        {
            Guid g => g,
            string s when Guid.TryParse(s, out var g) => g,
            _ => null
        };
    }

    private static bool? ReadBool(IReadOnlyDictionary<string, object?> fields, string key)
    {
        if (!fields.TryGetValue(key, out var raw) || raw is null)
            return null;

        return raw switch
        {
            bool b => b,
            string s when bool.TryParse(s, out var b) => b,
            _ => null
        };
    }
}
