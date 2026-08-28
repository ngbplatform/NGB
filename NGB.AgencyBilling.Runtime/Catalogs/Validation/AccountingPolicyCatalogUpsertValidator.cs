using NGB.Accounting.Accounts;
using NGB.AgencyBilling.Runtime.Validation;
using NGB.Definitions.Catalogs.Validation;
using NGB.OperationalRegisters.Contracts;
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

        var accountIds = new[] { cashAccountId, arAccountId, serviceRevenueAccountId };
        var accounts = await coaAdmin.GetByIdsAsync(accountIds, ct);

        EnsureAccount(accounts, cashAccountId, "cash_account_id", AccountType.Asset, mustNotRequireDimensions: true);
        EnsureAccount(accounts, arAccountId, "ar_account_id", AccountType.Asset, mustNotRequireDimensions: false);
        EnsureAccount(accounts, serviceRevenueAccountId, "service_revenue_account_id", AccountType.Income, mustNotRequireDimensions: false);

        var registerIds = new[]
        {
            projectTimeLedgerRegisterId,
            unbilledTimeRegisterId,
            projectBillingStatusRegisterId,
            arOpenItemsRegisterId
        };
        var registerMap = (await registers.GetByIdsAsync(registerIds, ct))
            .ToDictionary(x => x.RegisterId);

        EnsureRegister(registerMap, projectTimeLedgerRegisterId, "project_time_ledger_register_id", AgencyBillingCodes.ProjectTimeLedgerRegisterCode);
        EnsureRegister(registerMap, unbilledTimeRegisterId, "unbilled_time_register_id", AgencyBillingCodes.UnbilledTimeRegisterCode);
        EnsureRegister(registerMap, projectBillingStatusRegisterId, "project_billing_status_register_id", AgencyBillingCodes.ProjectBillingStatusRegisterCode);
        EnsureRegister(registerMap, arOpenItemsRegisterId, "ar_open_items_register_id", AgencyBillingCodes.ArOpenItemsRegisterCode);
    }

    private static void EnsureRegister(
        IReadOnlyDictionary<Guid, OperationalRegisterAdminItem> registers,
        Guid registerId,
        string fieldPath,
        string expectedCode)
    {
        if (!registers.TryGetValue(registerId, out var register))
            throw new NgbArgumentInvalidException(fieldPath, "Referenced operational register was not found.");

        if (!string.Equals(register.Code, expectedCode, StringComparison.OrdinalIgnoreCase))
            throw new NgbArgumentInvalidException(fieldPath, $"Referenced operational register must be '{expectedCode}'.");
    }

    private static Guid RequireGuid(IReadOnlyDictionary<string, object?> fields, string fieldPath, string message)
    {
        var value = AgencyBillingValidationValueReaders.ReadGuid(fields, fieldPath);
        if (!value.HasValue)
            throw new NgbArgumentInvalidException(fieldPath, message);

        if (value.Value == Guid.Empty)
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
