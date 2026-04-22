using NGB.Accounting.Accounts;
using NGB.AgencyBilling.Runtime.Validation;
using NGB.Definitions.Catalogs.Validation;
using NGB.Persistence.OperationalRegisters;
using NGB.Runtime.Accounts;
using NGB.Tools.Exceptions;

namespace NGB.AgencyBilling.Runtime.Catalogs.Validation;

public sealed class AccountingPolicyCatalogUpsertValidator(
    IChartOfAccountsAdminService coaAdmin,
    IOperationalRegisterRepository registers)
    : ICatalogUpsertValidator
{
    public string TypeCode => AgencyBillingCodes.AccountingPolicy;

    public async Task ValidateUpsertAsync(CatalogUpsertValidationContext context, CancellationToken ct)
    {
        if (!string.Equals(context.TypeCode, TypeCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new NgbConfigurationViolationException(
                $"{nameof(AccountingPolicyCatalogUpsertValidator)} is configured for '{TypeCode}', not '{context.TypeCode}'.");
        }

        var cashAccountId = RequireGuid(context.Fields, "cash_account_id", "Cash / Bank account is required.");
        var arAccountId = RequireGuid(context.Fields, "ar_account_id", "Accounts Receivable account is required.");
        var serviceRevenueAccountId = RequireGuid(context.Fields, "service_revenue_account_id", "Service Revenue account is required.");
        var projectTimeLedgerRegisterId = RequireGuid(context.Fields, "project_time_ledger_register_id", "Project Time Ledger register is required.");
        var unbilledTimeRegisterId = RequireGuid(context.Fields, "unbilled_time_register_id", "Unbilled Time register is required.");
        var projectBillingStatusRegisterId = RequireGuid(context.Fields, "project_billing_status_register_id", "Project Billing Status register is required.");
        var arOpenItemsRegisterId = RequireGuid(context.Fields, "ar_open_items_register_id", "AR Open Items register is required.");

        var defaultCurrency = AgencyBillingValidationValueReaders.ReadString(context.Fields, "default_currency");
        if (string.IsNullOrWhiteSpace(defaultCurrency))
            throw new NgbArgumentInvalidException("default_currency", "Default Currency is required.");

        var accounts = await coaAdmin.GetAsync(includeDeleted: true, ct);

        EnsureAccount(accounts, cashAccountId, "cash_account_id", AccountType.Asset, mustNotRequireDimensions: true);
        EnsureAccount(accounts, arAccountId, "ar_account_id", AccountType.Asset, mustNotRequireDimensions: false);
        EnsureAccount(accounts, serviceRevenueAccountId, "service_revenue_account_id", AccountType.Income, mustNotRequireDimensions: false);

        await EnsureRegisterAsync(projectTimeLedgerRegisterId, "project_time_ledger_register_id", AgencyBillingCodes.ProjectTimeLedgerRegisterCode, ct);
        await EnsureRegisterAsync(unbilledTimeRegisterId, "unbilled_time_register_id", AgencyBillingCodes.UnbilledTimeRegisterCode, ct);
        await EnsureRegisterAsync(projectBillingStatusRegisterId, "project_billing_status_register_id", AgencyBillingCodes.ProjectBillingStatusRegisterCode, ct);
        await EnsureRegisterAsync(arOpenItemsRegisterId, "ar_open_items_register_id", AgencyBillingCodes.ArOpenItemsRegisterCode, ct);
    }

    private async Task EnsureRegisterAsync(Guid registerId, string fieldPath, string expectedCode, CancellationToken ct)
    {
        var register = await registers.GetByIdAsync(registerId, ct);
        if (register is null)
            throw new NgbArgumentInvalidException(fieldPath, "Referenced operational register was not found.");

        if (!string.Equals(register.Code, expectedCode, StringComparison.OrdinalIgnoreCase))
            throw new NgbArgumentInvalidException(fieldPath, $"Referenced operational register must be '{expectedCode}'.");
    }

    private static Guid RequireGuid(IReadOnlyDictionary<string, object?> fields, string fieldPath, string message)
    {
        var value = AgencyBillingValidationValueReaders.ReadGuid(fields, fieldPath);
        if (value is null || value == Guid.Empty)
            throw new NgbArgumentInvalidException(fieldPath, message);

        return value.Value;
    }

    private static void EnsureAccount(
        IReadOnlyList<ChartOfAccountsAdminItem> accounts,
        Guid accountId,
        string fieldPath,
        AccountType expectedType,
        bool mustNotRequireDimensions)
    {
        var account = accounts.FirstOrDefault(x => x.Account.Id == accountId);
        if (account is null)
            throw new NgbArgumentInvalidException(fieldPath, "Referenced account was not found.");

        if (account.IsDeleted)
            throw new NgbArgumentInvalidException(fieldPath, "Referenced account is deleted.");

        if (!account.IsActive)
            throw new NgbArgumentInvalidException(fieldPath, "Referenced account is inactive.");

        if (account.Account.Type != expectedType)
            throw new NgbArgumentInvalidException(fieldPath, $"Referenced account must be of type '{expectedType}'.");

        if (mustNotRequireDimensions && account.Account.DimensionRules.Any(x => x.IsRequired))
            throw new NgbArgumentInvalidException(fieldPath, "Referenced account cannot require dimensions.");
    }
}
