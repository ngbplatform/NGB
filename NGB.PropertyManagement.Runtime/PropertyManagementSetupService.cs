using System.Text.Json;
using NGB.Accounting.Accounts;
using NGB.Accounting.CashFlow;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.OperationalRegisters;
using NGB.PropertyManagement.Contracts;
using NGB.Runtime.Accounts;
using NGB.Runtime.Accounts.Exceptions;
using NGB.Runtime.OperationalRegisters;
using NGB.Tools;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PropertyManagement.Runtime;

public sealed class PropertyManagementSetupService(
    IChartOfAccountsAdminService coaAdmin,
    IChartOfAccountsManagementService coaManagement,
    IOperationalRegisterManagementService opregManagement,
    IOperationalRegisterRepository opregRepo,
    IOperationalRegisterAdminMaintenanceService opregMaintenance,
    ICatalogService catalogs)
    : IPropertyManagementSetupService
{
    // Default template codes. Users may later change codes/names via admin UI,
    // but defaults must be present for an out-of-the-box experience.
    private const string AccountsReceivableTenantsCode = "1100";
    private const string PrepaidExpensesCode = "1300";
    private const string PropertyEquipmentCode = "1500";
    private const string AccountsPayableVendorsCode = "2000";
    private const string AccruedLiabilitiesCode = "2150";
    private const string LoanPayableCode = "2300";
    private const string OwnerEquityCode = "3000";
    private const string OwnerDistributionsCode = "3010";
    private const string RentalIncomeCode = "4000";
    private const string UtilityIncomeCode = "4010";
    private const string ParkingIncomeCode = "4020";
    private const string DamageIncomeCode = "4030";
    private const string MoveOutIncomeCode = "4040";
    private const string MiscIncomeCode = "4050";
    private const string LateFeeIncomeCode = "4100";
    private const string RepairsExpenseCode = "5100";
    private const string UtilitiesExpenseCode = "5200";
    private const string CleaningExpenseCode = "5300";
    private const string SuppliesExpenseCode = "5400";
    private const string DepreciationExpenseCode = "5800";
    private const string MiscExpenseCode = "5990";
    private const string CashAccountCode = "1000";

    private static readonly string[] ReceivablesDimensionCodes = [PropertyManagementCodes.Party, PropertyManagementCodes.Property, PropertyManagementCodes.Lease];
    private static readonly string[] PayablesDimensionCodes = [PropertyManagementCodes.Party, PropertyManagementCodes.Property];

    public async Task<PropertyManagementSetupResult> EnsureDefaultsAsync(CancellationToken ct = default)
    {
        // 1) Ensure CoA accounts exist (by code). If they exist but are incompatible, fail fast.
        var coa = await coaAdmin.GetAsync(includeDeleted: true, ct);

        var (arId, createdAr) = await EnsureAccountAsync(
            coa,
            code: AccountsReceivableTenantsCode,
            name: "Accounts Receivable - Tenants",
            type: AccountType.Asset,
            statementSection: StatementSection.Assets,
            requiredDimensions: ReceivablesDimensionCodes,
            ct: ct,
            cashFlowRole: CashFlowRole.WorkingCapital,
            cashFlowLineCode: CashFlowSystemLineCodes.WorkingCapitalAccountsReceivable);

        await EnsureAccountAsync(
            coa,
            code: PrepaidExpensesCode,
            name: "Prepaid Expenses",
            type: AccountType.Asset,
            statementSection: StatementSection.Assets,
            requiredDimensions: [],
            ct: ct,
            cashFlowRole: CashFlowRole.WorkingCapital,
            cashFlowLineCode: CashFlowSystemLineCodes.WorkingCapitalPrepaids);

        await EnsureAccountAsync(
            coa,
            code: PropertyEquipmentCode,
            name: "Property & Equipment",
            type: AccountType.Asset,
            statementSection: StatementSection.Assets,
            requiredDimensions: [],
            ct: ct,
            cashFlowRole: CashFlowRole.InvestingCounterparty,
            cashFlowLineCode: CashFlowSystemLineCodes.InvestingPropertyEquipmentNet);

        var (apId, createdAp) = await EnsureAccountAsync(
            coa,
            code: AccountsPayableVendorsCode,
            name: "Accounts Payable - Vendors",
            type: AccountType.Liability,
            statementSection: StatementSection.Liabilities,
            requiredDimensions: PayablesDimensionCodes,
            ct: ct,
            cashFlowRole: CashFlowRole.WorkingCapital,
            cashFlowLineCode: CashFlowSystemLineCodes.WorkingCapitalAccountsPayable);

        await EnsureAccountAsync(
            coa,
            code: AccruedLiabilitiesCode,
            name: "Accrued Liabilities",
            type: AccountType.Liability,
            statementSection: StatementSection.Liabilities,
            requiredDimensions: [],
            ct: ct,
            cashFlowRole: CashFlowRole.WorkingCapital,
            cashFlowLineCode: CashFlowSystemLineCodes.WorkingCapitalAccruedLiabilities);

        await EnsureAccountAsync(
            coa,
            code: LoanPayableCode,
            name: "Loan Payable",
            type: AccountType.Liability,
            statementSection: StatementSection.Liabilities,
            requiredDimensions: [],
            ct: ct,
            cashFlowRole: CashFlowRole.FinancingCounterparty,
            cashFlowLineCode: CashFlowSystemLineCodes.FinancingDebtNet);

        await EnsureAccountAsync(
            coa,
            code: OwnerEquityCode,
            name: "Owner Equity",
            type: AccountType.Equity,
            statementSection: StatementSection.Equity,
            requiredDimensions: [],
            ct: ct,
            cashFlowRole: CashFlowRole.FinancingCounterparty,
            cashFlowLineCode: CashFlowSystemLineCodes.FinancingOwnerEquityNet);

        await EnsureAccountAsync(
            coa,
            code: OwnerDistributionsCode,
            name: "Owner Distributions",
            type: AccountType.Equity,
            statementSection: StatementSection.Equity,
            requiredDimensions: [],
            ct: ct,
            cashFlowRole: CashFlowRole.FinancingCounterparty,
            cashFlowLineCode: CashFlowSystemLineCodes.FinancingDistributionsNet);

        var (incomeId, createdIncome) = await EnsureAccountAsync(
            coa,
            code: RentalIncomeCode,
            name: "Rental Income",
            type: AccountType.Income,
            statementSection: StatementSection.Income,
            requiredDimensions: ReceivablesDimensionCodes,
            ct);

        var (cashId, createdCash) = await EnsureCashAccountAsync(
            coa,
            code: CashAccountCode,
            name: "Operating Cash",
            ct);

        var (utilityIncomeId, _) = await EnsureAccountAsync(
            coa,
            code: UtilityIncomeCode,
            name: "Utility Income",
            type: AccountType.Income,
            statementSection: StatementSection.Income,
            requiredDimensions: ReceivablesDimensionCodes,
            ct);

        var (parkingIncomeId, _) = await EnsureAccountAsync(
            coa,
            code: ParkingIncomeCode,
            name: "Parking Income",
            type: AccountType.Income,
            statementSection: StatementSection.Income,
            requiredDimensions: ReceivablesDimensionCodes,
            ct);

        var (damageIncomeId, _) = await EnsureAccountAsync(
            coa,
            code: DamageIncomeCode,
            name: "Damage Income",
            type: AccountType.Income,
            statementSection: StatementSection.Income,
            requiredDimensions: ReceivablesDimensionCodes,
            ct);

        var (moveOutIncomeId, _) = await EnsureAccountAsync(
            coa,
            code: MoveOutIncomeCode,
            name: "Move Out Income",
            type: AccountType.Income,
            statementSection: StatementSection.Income,
            requiredDimensions: ReceivablesDimensionCodes,
            ct);

        var (miscIncomeId, _) = await EnsureAccountAsync(
            coa,
            code: MiscIncomeCode,
            name: "Miscellaneous Income",
            type: AccountType.Income,
            statementSection: StatementSection.Income,
            requiredDimensions: ReceivablesDimensionCodes,
            ct);

        var (lateFeeIncomeId, createdLateFeeIncome) = await EnsureAccountAsync(
            coa,
            code: LateFeeIncomeCode,
            name: "Late Fee Income",
            type: AccountType.Income,
            statementSection: StatementSection.OtherIncome,
            requiredDimensions: ReceivablesDimensionCodes,
            ct);

        var (repairsExpenseId, _) = await EnsureAccountAsync(
            coa,
            code: RepairsExpenseCode,
            name: "Repairs & Maintenance Expense",
            type: AccountType.Expense,
            statementSection: StatementSection.Expenses,
            requiredDimensions: PayablesDimensionCodes,
            ct);

        var (utilitiesExpenseId, _) = await EnsureAccountAsync(
            coa,
            code: UtilitiesExpenseCode,
            name: "Utilities Expense",
            type: AccountType.Expense,
            statementSection: StatementSection.Expenses,
            requiredDimensions: PayablesDimensionCodes,
            ct);

        var (cleaningExpenseId, _) = await EnsureAccountAsync(
            coa,
            code: CleaningExpenseCode,
            name: "Cleaning Expense",
            type: AccountType.Expense,
            statementSection: StatementSection.Expenses,
            requiredDimensions: PayablesDimensionCodes,
            ct);

        var (suppliesExpenseId, _) = await EnsureAccountAsync(
            coa,
            code: SuppliesExpenseCode,
            name: "Supplies Expense",
            type: AccountType.Expense,
            statementSection: StatementSection.Expenses,
            requiredDimensions: PayablesDimensionCodes,
            ct);

        await EnsureAccountAsync(
            coa,
            code: DepreciationExpenseCode,
            name: "Depreciation & Amortization Expense",
            type: AccountType.Expense,
            statementSection: StatementSection.Expenses,
            requiredDimensions: [],
            ct: ct,
            cashFlowRole: CashFlowRole.NonCashOperatingAdjustment,
            cashFlowLineCode: CashFlowSystemLineCodes.OperatingAdjustmentDepreciationAmortization);

        var (miscExpenseId, _) = await EnsureAccountAsync(
            coa,
            code: MiscExpenseCode,
            name: "Misc Expense",
            type: AccountType.Expense,
            statementSection: StatementSection.OtherExpense,
            requiredDimensions: PayablesDimensionCodes,
            ct);

        // 2) Ensure Operational Register exists + schema metadata
        var (opregId, createdOpreg) = await EnsureTenantBalancesOperationalRegisterAsync(ct);

        // Receivables open-items register (invoice-style AR).
        var (openItemsId, createdOpenItems) = await EnsureReceivablesOpenItemsOperationalRegisterAsync(ct);
        var (payablesOpenItemsId, createdPayablesOpenItems) = await EnsurePayablesOpenItemsOperationalRegisterAsync(ct);

        // 3) Ensure pm.accounting_policy exists as a single row (if multiple exist -> config violation)
        await EnsureDefaultReceivableChargeTypesAsync(
            [
                new SeededReceivableChargeType("Rent", incomeId),
                new SeededReceivableChargeType("Late Fee", lateFeeIncomeId),
                new SeededReceivableChargeType("Utility", utilityIncomeId),
                new SeededReceivableChargeType("Parking", parkingIncomeId),
                new SeededReceivableChargeType("Damage", damageIncomeId),
                new SeededReceivableChargeType("Move out", moveOutIncomeId),
                new SeededReceivableChargeType("Misc", miscIncomeId)
            ],
            ct);

        await EnsureDefaultPayableChargeTypesAsync(
            [
                new SeededPayableChargeType("Repair", repairsExpenseId),
                new SeededPayableChargeType("Utility", utilitiesExpenseId),
                new SeededPayableChargeType("Cleaning", cleaningExpenseId),
                new SeededPayableChargeType("Supply", suppliesExpenseId),
                new SeededPayableChargeType("Misc", miscExpenseId)
            ],
            ct);

        await EnsureDefaultMaintenanceCategoriesAsync(
            [
                "Plumbing",
                "Electrical",
                "HVAC",
                "Appliance",
                "General",
                "Lock / Security"
            ],
            ct);

        var (defaultBankAccountId, createdDefaultBankAccount) = await EnsureDefaultBankAccountAsync(cashId, ct);
        await EnsureBankAccountGlAccountsAsync(ct);
        var (policyId, createdPolicy) = await EnsureAccountingPolicyAsync(cashId, arId, apId, incomeId, lateFeeIncomeId, opregId, openItemsId, payablesOpenItemsId, ct);

        return new PropertyManagementSetupResult(
            CashAccountId: cashId,
            DefaultBankAccountCatalogId: defaultBankAccountId,
            AccountsReceivableTenantsAccountId: arId,
            AccountsPayableVendorsAccountId: apId,
            RentalIncomeAccountId: incomeId,
            LateFeeIncomeAccountId: lateFeeIncomeId,
            PayablesOpenItemsOperationalRegisterId: payablesOpenItemsId,
            TenantBalancesOperationalRegisterId: opregId,
            ReceivablesOpenItemsOperationalRegisterId: openItemsId,
            AccountingPolicyCatalogId: policyId,
            CreatedCashAccount: createdCash,
            CreatedDefaultBankAccount: createdDefaultBankAccount,
            CreatedAccountsReceivableTenants: createdAr,
            CreatedAccountsPayableVendors: createdAp,
            CreatedRentalIncome: createdIncome,
            CreatedLateFeeIncome: createdLateFeeIncome,
            CreatedPayablesOpenItemsOperationalRegister: createdPayablesOpenItems,
            CreatedTenantBalancesOperationalRegister: createdOpreg,
            CreatedReceivablesOpenItemsOperationalRegister: createdOpenItems,
            CreatedAccountingPolicy: createdPolicy);
    }

    private async Task<(Guid Id, bool Created)> EnsureAccountAsync(
        IReadOnlyList<ChartOfAccountsAdminItem> coa,
        string code,
        string name,
        AccountType type,
        StatementSection statementSection,
        IReadOnlyList<string> requiredDimensions,
        CancellationToken ct,
        CashFlowRole cashFlowRole = CashFlowRole.None,
        string? cashFlowLineCode = null)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new NgbArgumentRequiredException(nameof(code));

        var existing = coa.FirstOrDefault(x => string.Equals(x.Account.Code, code, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            // If the account exists but is deleted/inactive, we still allow setup to proceed,
            // but the policy will point to it and posting will fail. Better to be explicit:
            if (existing.IsDeleted)
                throw new NgbConfigurationViolationException($"Chart of Accounts account '{code}' exists but is marked for deletion.");

            if (!existing.IsActive)
                throw new NgbConfigurationViolationException($"Chart of Accounts account '{code}' exists but is inactive.");

            if (existing.Account.Type != type)
                throw new NgbConfigurationViolationException($"Chart of Accounts account '{code}' has unexpected type. Expected '{type}', actual '{existing.Account.Type}'.");

            if (existing.Account.StatementSection != statementSection)
                throw new NgbConfigurationViolationException($"Chart of Accounts account '{code}' has unexpected statement section. Expected '{statementSection}', actual '{existing.Account.StatementSection}'.");

            // NOTE: Older DB snapshots may already contain accounts created manually without the
            // required PM analytical dimensions. ApplyDefaults repairs them when possible (no movements).
            await EnsureOrRepairRequiredDimensionsAsync(existing, requiredDimensions, ct);
            await EnsureOrRepairCashFlowMetadataAsync(existing, cashFlowRole, cashFlowLineCode, ct);

            return (existing.Account.Id, false);
        }

        var id = await coaManagement.CreateAsync(
            new CreateAccountRequest(
                Code: code,
                Name: name,
                Type: type,
                StatementSection: statementSection,
                IsContra: false,
                NegativeBalancePolicy: null,
                IsActive: true,
                DimensionRules: requiredDimensions.Select((x, i) => new AccountDimensionRuleRequest(x, IsRequired: true, Ordinal: i + 1)).ToArray(),
                CashFlowRole: cashFlowRole,
                CashFlowLineCode: cashFlowLineCode),
            ct);

        return (id, true);
    }

    private async Task<(Guid Id, bool Created)> EnsureCashAccountAsync(
        IReadOnlyList<ChartOfAccountsAdminItem> coa,
        string code,
        string name,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new NgbArgumentRequiredException(nameof(code));

        var existing = coa.FirstOrDefault(x => string.Equals(x.Account.Code, code, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            if (existing.IsDeleted)
                throw new NgbConfigurationViolationException($"Chart of Accounts account '{code}' exists but is marked for deletion.");

            if (!existing.IsActive)
                throw new NgbConfigurationViolationException($"Chart of Accounts account '{code}' exists but is inactive.");

            if (existing.Account.Type != AccountType.Asset)
                throw new NgbConfigurationViolationException($"Chart of Accounts account '{code}' has unexpected type. Expected '{AccountType.Asset}', actual '{existing.Account.Type}'.");

            if (existing.Account.StatementSection != StatementSection.Assets)
                throw new NgbConfigurationViolationException($"Chart of Accounts account '{code}' has unexpected statement section. Expected '{StatementSection.Assets}', actual '{existing.Account.StatementSection}'.");

            // IMPORTANT: payment posting uses DimensionBag.Empty on the cash side.
            // If cash account has required dimensions, posting would fail.
            if (existing.Account.DimensionRules.Any(x => x.IsRequired))
                throw new NgbConfigurationViolationException($"Chart of Accounts account '{code}' must not require dimensions (PM cash control account).");

            await EnsureOrRepairCashFlowMetadataAsync(existing, CashFlowRole.CashEquivalent, expectedLineCode: null, ct);
            return (existing.Account.Id, false);
        }

        var id = await coaManagement.CreateAsync(
            new CreateAccountRequest(
                Code: code,
                Name: name,
                Type: AccountType.Asset,
                StatementSection: StatementSection.Assets,
                IsContra: false,
                NegativeBalancePolicy: null,
                IsActive: true,
                DimensionRules: [],
                CashFlowRole: CashFlowRole.CashEquivalent),
            ct);

        return (id, true);
    }

    private static void EnsureHasRequiredDimension(
        Account account,
        string dimensionCode,
        IReadOnlyList<string> requiredDimensions)
    {
        if (account.DimensionRules is null || account.DimensionRules.Count == 0)
            throw new NgbConfigurationViolationException($"Chart of Accounts account '{account.Code}' is missing dimension rules. Required: {string.Join(", ", requiredDimensions)}.");

        var rule = account.DimensionRules.FirstOrDefault(x => string.Equals(x.DimensionCode, dimensionCode, StringComparison.OrdinalIgnoreCase));
        if (rule is null)
            throw new NgbConfigurationViolationException($"Chart of Accounts account '{account.Code}' is missing required dimension '{dimensionCode}'.");

        if (!rule.IsRequired)
            throw new NgbConfigurationViolationException($"Chart of Accounts account '{account.Code}' dimension '{dimensionCode}' must be required for PM documents.");
    }

    private async Task EnsureOrRepairRequiredDimensionsAsync(
        ChartOfAccountsAdminItem existing,
        IReadOnlyList<string> requiredDimensions,
        CancellationToken ct)
    {
        if (requiredDimensions.All(x => HasRequiredDimension(existing.Account, x)))
            return;

        try
        {
            await coaManagement.UpdateAsync(
                new UpdateAccountRequest(
                    AccountId: existing.Account.Id,
                    DimensionRules: requiredDimensions.Select((x, i) => new AccountDimensionRuleRequest(x, IsRequired: true, Ordinal: i + 1)).ToArray()),
                ct);
        }
        catch (AccountHasMovementsImmutabilityViolationException ex)
        {
            throw new NgbConfigurationViolationException(
                message: $"Chart of Accounts account '{existing.Account.Code}' exists but is incompatible with PM defaults (missing required dimensions: {string.Join(", ", requiredDimensions)}). " +
                         "The account has movements, so dimension rules cannot be updated automatically. " +
                         "Create a new compatible account and update Accounting Policy to reference it.",
                innerException: ex);
        }

        var refreshed = (await coaAdmin.GetAsync(includeDeleted: true, ct))
            .FirstOrDefault(x => string.Equals(x.Account.Code, existing.Account.Code, StringComparison.OrdinalIgnoreCase));

        if (refreshed is null)
            throw new NgbConfigurationViolationException($"Chart of Accounts account '{existing.Account.Code}' disappeared during ApplyDefaults.");

        foreach (var dimension in requiredDimensions)
        {
            EnsureHasRequiredDimension(refreshed.Account, dimension, requiredDimensions);
        }
    }

    private async Task EnsureOrRepairCashFlowMetadataAsync(
        ChartOfAccountsAdminItem existing,
        CashFlowRole expectedRole,
        string? expectedLineCode,
        CancellationToken ct)
    {
        var actualRole = existing.Account.CashFlowRole;
        var actualLineCode = NormalizeLineCode(existing.Account.CashFlowLineCode);
        var normalizedExpectedLineCode = NormalizeLineCode(expectedLineCode);

        if (actualRole == expectedRole && string.Equals(actualLineCode, normalizedExpectedLineCode, StringComparison.Ordinal))
            return;

        try
        {
            await coaManagement.UpdateAsync(
                new UpdateAccountRequest(
                    AccountId: existing.Account.Id,
                    CashFlowRole: expectedRole,
                    CashFlowLineCode: normalizedExpectedLineCode ?? string.Empty),
                ct);
        }
        catch (AccountHasMovementsImmutabilityViolationException ex)
        {
            throw new NgbConfigurationViolationException(
                message: $"Chart of Accounts account '{existing.Account.Code}' exists but has incompatible cash flow metadata. " +
                         $"Expected role '{expectedRole}' and line '{normalizedExpectedLineCode ?? "<none>"}', actual role '{actualRole}' and line '{actualLineCode ?? "<none>"}'. " +
                         "The account has movements, so cash flow metadata cannot be updated automatically. " +
                         "Create a new compatible account and update Accounting Policy to reference it.",
                innerException: ex);
        }

        var refreshed = (await coaAdmin.GetAsync(includeDeleted: true, ct))
            .FirstOrDefault(x => string.Equals(x.Account.Code, existing.Account.Code, StringComparison.OrdinalIgnoreCase));

        if (refreshed is null)
            throw new NgbConfigurationViolationException($"Chart of Accounts account '{existing.Account.Code}' disappeared during ApplyDefaults.");

        if (refreshed.Account.CashFlowRole != expectedRole
            || !string.Equals(NormalizeLineCode(refreshed.Account.CashFlowLineCode), normalizedExpectedLineCode, StringComparison.Ordinal))
        {
            throw new NgbConfigurationViolationException(
                $"Chart of Accounts account '{existing.Account.Code}' cash flow metadata could not be repaired automatically.");
        }
    }

    private static string? NormalizeLineCode(string? lineCode)
        => string.IsNullOrWhiteSpace(lineCode) ? null : lineCode.Trim();

    private static bool HasRequiredDimension(Account account, string dimensionCode)
    {
        if (account.DimensionRules is null || account.DimensionRules.Count == 0)
            return false;

        var rule = account.DimensionRules.FirstOrDefault(x => string.Equals(x.DimensionCode, dimensionCode, StringComparison.OrdinalIgnoreCase));
        return rule is not null && rule.IsRequired;
    }

    private async Task<(Guid Id, bool Created)> EnsureTenantBalancesOperationalRegisterAsync(CancellationToken ct)
    {
        var existed = await opregRepo.GetByCodeAsync(PropertyManagementCodes.TenantBalancesRegisterCode, ct);

        var id = await opregManagement.UpsertAsync(PropertyManagementCodes.TenantBalancesRegisterCode, "Tenant Balances", ct);

        // Resource: amount
        await opregManagement.ReplaceResourcesAsync(
            id,
            [new OperationalRegisterResourceDefinition("amount", "Amount", 1)],
            ct);

        // Dimension rules (required)
        await opregManagement.ReplaceDimensionRulesAsync(
            id,
            [
                new OperationalRegisterDimensionRule(
                    DimensionId: DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Party}"),
                    DimensionCode: PropertyManagementCodes.Party,
                    Ordinal: 1,
                    IsRequired: true),
                new OperationalRegisterDimensionRule(
                    DimensionId: DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Property}"),
                    DimensionCode: PropertyManagementCodes.Property,
                    Ordinal: 2,
                    IsRequired: true),
                new OperationalRegisterDimensionRule(
                    DimensionId: DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Lease}"),
                    DimensionCode: PropertyManagementCodes.Lease,
                    Ordinal: 3,
                    IsRequired: true)
            ],
            ct);

        return (id, existed is null);
    }

    private async Task<(Guid Id, bool Created)> EnsureReceivablesOpenItemsOperationalRegisterAsync(CancellationToken ct)
    {
        var existed = await opregRepo.GetByCodeAsync(PropertyManagementCodes.ReceivablesOpenItemsRegisterCode, ct);

        var id = await opregManagement.UpsertAsync(PropertyManagementCodes.ReceivablesOpenItemsRegisterCode, "Receivables - Open Items", ct);

        // Resource: amount (signed; charges add +amount, applications subtract -amount, credits are negative).
        await opregManagement.ReplaceResourcesAsync(
            id,
            [new OperationalRegisterResourceDefinition("amount", "Amount", 1)],
            ct);

        // Dimension rules (required): party/property/lease + receivable_item (open item id).
        await opregManagement.ReplaceDimensionRulesAsync(
            id,
            [
                new OperationalRegisterDimensionRule(
                    DimensionId: DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Party}"),
                    DimensionCode: PropertyManagementCodes.Party,
                    Ordinal: 1,
                    IsRequired: true),
                new OperationalRegisterDimensionRule(
                    DimensionId: DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Property}"),
                    DimensionCode: PropertyManagementCodes.Property,
                    Ordinal: 2,
                    IsRequired: true),
                new OperationalRegisterDimensionRule(
                    DimensionId: DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Lease}"),
                    DimensionCode: PropertyManagementCodes.Lease,
                    Ordinal: 3,
                    IsRequired: true),
                new OperationalRegisterDimensionRule(
                    DimensionId: DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.ReceivableItem}"),
                    DimensionCode: PropertyManagementCodes.ReceivableItem,
                    Ordinal: 4,
                    IsRequired: true)
            ],
            ct);

        // Ensure physical per-register tables now (so the first posting doesn't pay the schema-creation tax).
        await opregMaintenance.EnsurePhysicalSchemaByIdAsync(id, ct);

        return (id, existed is null);
    }

    private async Task<(Guid Id, bool Created)> EnsurePayablesOpenItemsOperationalRegisterAsync(CancellationToken ct)
    {
        var existed = await opregRepo.GetByCodeAsync(PropertyManagementCodes.PayablesOpenItemsRegisterCode, ct);

        var id = await opregManagement.UpsertAsync(PropertyManagementCodes.PayablesOpenItemsRegisterCode, "Payables - Open Items", ct);

        await opregManagement.ReplaceResourcesAsync(
            id,
            [new OperationalRegisterResourceDefinition("amount", "Amount", 1)],
            ct);

        await opregManagement.ReplaceDimensionRulesAsync(
            id,
            [
                new OperationalRegisterDimensionRule(
                    DimensionId: DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Party}"),
                    DimensionCode: PropertyManagementCodes.Party,
                    Ordinal: 1,
                    IsRequired: true),
                new OperationalRegisterDimensionRule(
                    DimensionId: DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Property}"),
                    DimensionCode: PropertyManagementCodes.Property,
                    Ordinal: 2,
                    IsRequired: true),
                new OperationalRegisterDimensionRule(
                    DimensionId: DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.PayableItem}"),
                    DimensionCode: PropertyManagementCodes.PayableItem,
                    Ordinal: 3,
                    IsRequired: true)
            ],
            ct);

        await opregMaintenance.EnsurePhysicalSchemaByIdAsync(id, ct);

        return (id, existed is null);
    }

    private async Task EnsureDefaultReceivableChargeTypesAsync(
        IReadOnlyList<SeededReceivableChargeType> defaults,
        CancellationToken ct)
    {
        // Best-effort seed for out-of-the-box UX. Users can create more charge types in the UI.
        var page = await catalogs.GetPageAsync(
            PropertyManagementCodes.ReceivableChargeType,
            new PageRequestDto(Offset: 0, Limit: 200, Search: null),
            ct);

        foreach (var def in defaults)
        {
            var existing = page.Items.FirstOrDefault(x =>
                string.Equals(x.Display, def.Display, StringComparison.OrdinalIgnoreCase));

            var payload = new RecordPayload(
                Fields: new Dictionary<string, JsonElement>
                {
                    ["display"] = JsonTools.J(def.Display),
                    ["credit_account_id"] = JsonTools.J(def.CreditAccountId)
                },
                Parts: null);

            if (existing is not null)
            {
                await catalogs.UpdateAsync(PropertyManagementCodes.ReceivableChargeType, existing.Id, payload, ct);
                continue;
            }

            await catalogs.CreateAsync(PropertyManagementCodes.ReceivableChargeType, payload, ct);
        }
    }

    private async Task EnsureDefaultPayableChargeTypesAsync(
        IReadOnlyList<SeededPayableChargeType> defaults,
        CancellationToken ct)
    {
        var page = await catalogs.GetPageAsync(
            PropertyManagementCodes.PayableChargeType,
            new PageRequestDto(Offset: 0, Limit: 200, Search: null),
            ct);

        foreach (var def in defaults)
        {
            var existing = page.Items.FirstOrDefault(x =>
                string.Equals(x.Display, def.Display, StringComparison.OrdinalIgnoreCase));

            var payload = new RecordPayload(
                Fields: new Dictionary<string, JsonElement>
                {
                    ["display"] = JsonTools.J(def.Display),
                    ["debit_account_id"] = JsonTools.J(def.DebitAccountId)
                },
                Parts: null);

            if (existing is not null)
            {
                await catalogs.UpdateAsync(PropertyManagementCodes.PayableChargeType, existing.Id, payload, ct);
                continue;
            }

            await catalogs.CreateAsync(PropertyManagementCodes.PayableChargeType, payload, ct);
        }
    }

    private async Task EnsureDefaultMaintenanceCategoriesAsync(
        IReadOnlyList<string> defaults,
        CancellationToken ct)
    {
        var page = await catalogs.GetPageAsync(
            PropertyManagementCodes.MaintenanceCategory,
            new PageRequestDto(Offset: 0, Limit: 200, Search: null),
            ct);

        foreach (var display in defaults)
        {
            var existing = page.Items.FirstOrDefault(x =>
                string.Equals(x.Display, display, StringComparison.OrdinalIgnoreCase));

            var payload = new RecordPayload(
                Fields: new Dictionary<string, JsonElement>
                {
                    ["display"] = JsonTools.J(display)
                },
                Parts: null);

            if (existing is not null)
            {
                await catalogs.UpdateAsync(PropertyManagementCodes.MaintenanceCategory, existing.Id, payload, ct);
                continue;
            }

            await catalogs.CreateAsync(PropertyManagementCodes.MaintenanceCategory, payload, ct);
        }
    }

    private async Task<(Guid Id, bool Created)> EnsureDefaultBankAccountAsync(Guid cashId, CancellationToken ct)
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

        if (page.Items.Count > 1)
            throw new NgbConfigurationViolationException($"Multiple active '{PropertyManagementCodes.BankAccount}' records are marked as default. Expected a single default record.");

        var payload = new RecordPayload(
            Fields: new Dictionary<string, JsonElement>
            {
                ["display"] = JsonTools.J("Operating Account"),
                ["bank_name"] = JsonTools.J("Harbor State Bank"),
                ["account_name"] = JsonTools.J("Operating Account"),
                ["last4"] = JsonTools.J("1000"),
                ["gl_account_id"] = JsonTools.J(cashId),
                ["is_default"] = JsonTools.J(true)
            },
            Parts: null);

        if (page.Items.Count == 1)
        {
            var existing = page.Items[0];
            await catalogs.UpdateAsync(PropertyManagementCodes.BankAccount, existing.Id, payload, ct);
            return (existing.Id, false);
        }

        var created = await catalogs.CreateAsync(PropertyManagementCodes.BankAccount, payload, ct);
        return (created.Id, true);
    }

    private async Task EnsureBankAccountGlAccountsAsync(CancellationToken ct)
    {
        var coa = await coaAdmin.GetAsync(includeDeleted: true, ct);

        const int pageSize = 200;
        for (var offset = 0; ; offset += pageSize)
        {
            var page = await catalogs.GetPageAsync(
                PropertyManagementCodes.BankAccount,
                new PageRequestDto(
                    Offset: offset,
                    Limit: pageSize,
                    Search: null,
                    Filters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["deleted"] = "active"
                    }),
                ct);

            foreach (var bankAccount in page.Items)
            {
                var fields = bankAccount.Payload.Fields;
                if (fields is null || !fields.TryGetValue("gl_account_id", out var glAccountField))
                    throw new NgbConfigurationViolationException($"Bank account '{bankAccount.Id}' is missing required field 'gl_account_id'.");

                Guid glAccountId;
                try
                {
                    glAccountId = glAccountField.ParseGuidOrRef();
                }
                catch (Exception ex)
                {
                    throw new NgbConfigurationViolationException(
                        $"Bank account '{bankAccount.Id}' has invalid 'gl_account_id'.",
                        innerException: ex);
                }

                var linkedAccount = coa.FirstOrDefault(x => x.Account.Id == glAccountId)
                                    ?? throw new NgbConfigurationViolationException(
                                        $"Bank account '{bankAccount.Display ?? bankAccount.Id.ToString()}' references missing GL account '{glAccountId}'.");

                if (linkedAccount.IsDeleted)
                    throw new NgbConfigurationViolationException(
                        $"Bank account '{bankAccount.Display ?? bankAccount.Id.ToString()}' references GL account '{linkedAccount.Account.Code}' that is marked for deletion.");

                if (!linkedAccount.IsActive)
                    throw new NgbConfigurationViolationException(
                        $"Bank account '{bankAccount.Display ?? bankAccount.Id.ToString()}' references GL account '{linkedAccount.Account.Code}' that is inactive.");

                if (linkedAccount.Account.Type != AccountType.Asset)
                    throw new NgbConfigurationViolationException(
                        $"Bank account '{bankAccount.Display ?? bankAccount.Id.ToString()}' references GL account '{linkedAccount.Account.Code}' with unexpected type '{linkedAccount.Account.Type}'. Expected '{AccountType.Asset}'.");

                if (linkedAccount.Account.StatementSection != StatementSection.Assets)
                    throw new NgbConfigurationViolationException(
                        $"Bank account '{bankAccount.Display ?? bankAccount.Id.ToString()}' references GL account '{linkedAccount.Account.Code}' with unexpected statement section '{linkedAccount.Account.StatementSection}'. Expected '{StatementSection.Assets}'.");

                await EnsureOrRepairCashFlowMetadataAsync(linkedAccount, CashFlowRole.CashEquivalent, expectedLineCode: null, ct);
            }

            if (page.Items.Count < pageSize)
                break;
        }
    }

    private async Task<(Guid Id, bool Created)> EnsureAccountingPolicyAsync(
        Guid cashId,
        Guid arId,
        Guid apId,
        Guid incomeId,
        Guid lateFeeIncomeId,
        Guid opregId,
        Guid openItemsId,
        Guid payablesOpenItemsId,
        CancellationToken ct)
    {
        // Single-record policy: page size 2 is enough to detect duplicates.
        var page = await catalogs.GetPageAsync(
            PropertyManagementCodes.AccountingPolicy,
            new PageRequestDto(Offset: 0, Limit: 2, Search: null),
            ct);

        if (page.Items.Count > 1)
            throw new NgbConfigurationViolationException($"Multiple '{PropertyManagementCodes.AccountingPolicy}' records exist. Expected a single record.");

        var payload = new RecordPayload(
            Fields: new Dictionary<string, JsonElement>
            {
                ["display"] = JsonTools.J("Property Management - Accounting Policy"),
                ["cash_account_id"] = JsonTools.J(cashId),
                ["ar_tenants_account_id"] = JsonTools.J(arId),
                ["ap_vendors_account_id"] = JsonTools.J(apId),
                ["rent_income_account_id"] = JsonTools.J(incomeId),
                ["late_fee_income_account_id"] = JsonTools.J(lateFeeIncomeId),
                ["tenant_balances_register_id"] = JsonTools.J(opregId),
                ["receivables_open_items_register_id"] = JsonTools.J(openItemsId),
                ["payables_open_items_register_id"] = JsonTools.J(payablesOpenItemsId),
            },
            Parts: null);

        if (page.Items.Count == 1)
        {
            var existing = page.Items[0];
            await catalogs.UpdateAsync(PropertyManagementCodes.AccountingPolicy, existing.Id, payload, ct);
            return (existing.Id, false);
        }

        var created = await catalogs.CreateAsync(PropertyManagementCodes.AccountingPolicy, payload, ct);
        return (created.Id, true);
    }

    private sealed record SeededReceivableChargeType(string Display, Guid CreditAccountId);
    
    private sealed record SeededPayableChargeType(string Display, Guid DebitAccountId);
}
