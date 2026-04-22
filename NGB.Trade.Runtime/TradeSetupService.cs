using System.Text.Json;
using NGB.Accounting.Accounts;
using NGB.Accounting.CashFlow;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.ReferenceRegisters;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.Runtime.Accounts;
using NGB.Runtime.Accounts.Exceptions;
using NGB.Runtime.OperationalRegisters;
using NGB.Runtime.ReferenceRegisters;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;
using NGB.Trade.Contracts;

namespace NGB.Trade.Runtime;

public sealed class TradeSetupService(
    IChartOfAccountsAdminService coaAdmin,
    IChartOfAccountsManagementService coaManagement,
    IOperationalRegisterManagementService opregManagement,
    IOperationalRegisterRepository opregRepo,
    IOperationalRegisterAdminMaintenanceService opregMaintenance,
    IReferenceRegisterManagementService refregManagement,
    IReferenceRegisterRepository refregRepo,
    IReferenceRegisterAdminMaintenanceService refregMaintenance,
    ICatalogService catalogs)
    : ITradeSetupService
{
    private const string CashCode = "1000";
    private const string AccountsReceivableCode = "1100";
    private const string InventoryCode = "1200";
    private const string AccountsPayableCode = "2000";
    private const string SalesRevenueCode = "4000";
    private const string CostOfGoodsSoldCode = "5000";
    private const string InventoryAdjustmentCode = "5200";

    private static readonly string[] PartyDimensions = [TradeCodes.Party];
    private static readonly string[] InventoryDimensions = [TradeCodes.Item, TradeCodes.Warehouse];
    private static readonly string[] SalesDimensions = [TradeCodes.Party, TradeCodes.Item, TradeCodes.Warehouse];

    public async Task<TradeSetupResult> EnsureDefaultsAsync(CancellationToken ct = default)
    {
        var coa = await coaAdmin.GetAsync(includeDeleted: true, ct);

        var (cashId, createdCash) = await EnsureCashAccountAsync(coa, CashCode, "Operating Cash", ct);
        var (arId, createdAr) = await EnsureAccountAsync(
            coa,
            AccountsReceivableCode,
            "Accounts Receivable",
            AccountType.Asset,
            StatementSection.Assets,
            PartyDimensions,
            ct,
            CashFlowRole.WorkingCapital,
            CashFlowSystemLineCodes.WorkingCapitalAccountsReceivable);
        var (inventoryId, createdInventory) = await EnsureAccountAsync(
            coa,
            InventoryCode,
            "Inventory",
            AccountType.Asset,
            StatementSection.Assets,
            InventoryDimensions,
            ct,
            CashFlowRole.WorkingCapital,
            CashFlowSystemLineCodes.WorkingCapitalInventory);
        var (apId, createdAp) = await EnsureAccountAsync(
            coa,
            AccountsPayableCode,
            "Accounts Payable",
            AccountType.Liability,
            StatementSection.Liabilities,
            PartyDimensions,
            ct,
            CashFlowRole.WorkingCapital,
            CashFlowSystemLineCodes.WorkingCapitalAccountsPayable);
        var (salesRevenueId, createdSalesRevenue) = await EnsureAccountAsync(
            coa,
            SalesRevenueCode,
            "Sales Revenue",
            AccountType.Income,
            StatementSection.Income,
            SalesDimensions,
            ct);
        var (cogsId, createdCogs) = await EnsureAccountAsync(
            coa,
            CostOfGoodsSoldCode,
            "Cost of Goods Sold",
            AccountType.Expense,
            StatementSection.Expenses,
            InventoryDimensions,
            ct);
        var (inventoryAdjustmentId, createdInventoryAdjustment) = await EnsureAccountAsync(
            coa,
            InventoryAdjustmentCode,
            "Inventory Adjustment Expense / Gain-Loss",
            AccountType.Expense,
            StatementSection.Expenses,
            InventoryDimensions,
            ct);

        var (inventoryMovementsId, createdInventoryMovements) = await EnsureInventoryMovementsOperationalRegisterAsync(ct);
        var (itemPricesId, createdItemPrices) = await EnsureItemPricesReferenceRegisterAsync(ct);
        var (policyId, createdPolicy) = await EnsureAccountingPolicyAsync(
            cashId,
            arId,
            inventoryId,
            apId,
            salesRevenueId,
            cogsId,
            inventoryAdjustmentId,
            inventoryMovementsId,
            itemPricesId,
            ct);

        await EnsureDefaultUnitOfMeasuresAsync(ct);
        await EnsureDefaultPaymentTermsAsync(ct);
        await EnsureDefaultInventoryAdjustmentReasonsAsync(ct);
        await EnsureDefaultPriceTypesAsync(ct);

        return new TradeSetupResult(
            CashAccountId: cashId,
            AccountsReceivableAccountId: arId,
            InventoryAccountId: inventoryId,
            AccountsPayableAccountId: apId,
            SalesRevenueAccountId: salesRevenueId,
            CostOfGoodsSoldAccountId: cogsId,
            InventoryAdjustmentAccountId: inventoryAdjustmentId,
            InventoryMovementsOperationalRegisterId: inventoryMovementsId,
            ItemPricesReferenceRegisterId: itemPricesId,
            AccountingPolicyCatalogId: policyId,
            CreatedCashAccount: createdCash,
            CreatedAccountsReceivableAccount: createdAr,
            CreatedInventoryAccount: createdInventory,
            CreatedAccountsPayableAccount: createdAp,
            CreatedSalesRevenueAccount: createdSalesRevenue,
            CreatedCostOfGoodsSoldAccount: createdCogs,
            CreatedInventoryAdjustmentAccount: createdInventoryAdjustment,
            CreatedInventoryMovementsOperationalRegister: createdInventoryMovements,
            CreatedItemPricesReferenceRegister: createdItemPrices,
            CreatedAccountingPolicy: createdPolicy);
    }

    private async Task<(Guid Id, bool Created)> EnsureInventoryMovementsOperationalRegisterAsync(CancellationToken ct)
    {
        var existed = await opregRepo.GetByCodeAsync(TradeCodes.InventoryMovementsRegisterCode, ct);

        var id = await opregManagement.UpsertAsync(TradeCodes.InventoryMovementsRegisterCode, "Inventory Movements", ct);
        await opregManagement.ReplaceResourcesAsync(
            id,
            [
                new OperationalRegisterResourceDefinition("qty_in", "Quantity In", 1),
                new OperationalRegisterResourceDefinition("qty_out", "Quantity Out", 2),
                new OperationalRegisterResourceDefinition("qty_delta", "Quantity Delta", 3)
            ],
            ct);
        await opregManagement.ReplaceDimensionRulesAsync(
            id,
            [
                new OperationalRegisterDimensionRule(
                    DimensionId: DeterministicGuid.Create($"Dimension|{TradeCodes.Item}"),
                    DimensionCode: TradeCodes.Item,
                    Ordinal: 1,
                    IsRequired: true),
                new OperationalRegisterDimensionRule(
                    DimensionId: DeterministicGuid.Create($"Dimension|{TradeCodes.Warehouse}"),
                    DimensionCode: TradeCodes.Warehouse,
                    Ordinal: 2,
                    IsRequired: true)
            ],
            ct);

        await opregMaintenance.EnsurePhysicalSchemaByIdAsync(id, ct);
        return (id, existed is null);
    }

    private async Task<(Guid Id, bool Created)> EnsureItemPricesReferenceRegisterAsync(CancellationToken ct)
    {
        var existed = await refregRepo.GetByCodeAsync(TradeCodes.ItemPricesRegisterCode, ct);
        var id = await refregManagement.UpsertAsync(
            TradeCodes.ItemPricesRegisterCode,
            "Item Prices",
            ReferenceRegisterPeriodicity.NonPeriodic,
            ReferenceRegisterRecordMode.Independent,
            ct);

        await refregManagement.ReplaceFieldsAsync(
            id,
            [
                new ReferenceRegisterFieldDefinition("currency", "Currency", 1, Metadata.Base.ColumnType.String, false),
                new ReferenceRegisterFieldDefinition("unit_price", "Unit Price", 2, Metadata.Base.ColumnType.Decimal, false),
                new ReferenceRegisterFieldDefinition("effective_date", "Effective Date", 3, Metadata.Base.ColumnType.Date, false),
                new ReferenceRegisterFieldDefinition("source_document_id", "Source Document Id", 4, Metadata.Base.ColumnType.Guid, false),
                new ReferenceRegisterFieldDefinition("updated_at_utc", "Updated At", 5, Metadata.Base.ColumnType.DateTimeUtc, false)
            ],
            ct);

        await refregManagement.ReplaceDimensionRulesAsync(
            id,
            [
                new ReferenceRegisterDimensionRule(
                    DimensionId: DeterministicGuid.Create($"Dimension|{TradeCodes.Item}"),
                    DimensionCode: TradeCodes.Item,
                    Ordinal: 1,
                    IsRequired: true),
                new ReferenceRegisterDimensionRule(
                    DimensionId: DeterministicGuid.Create($"Dimension|{TradeCodes.PriceType}"),
                    DimensionCode: TradeCodes.PriceType,
                    Ordinal: 2,
                    IsRequired: true)
            ],
            ct);

        await refregMaintenance.EnsurePhysicalSchemaByIdAsync(id, ct);
        return (id, existed is null);
    }

    private async Task<(Guid Id, bool Created)> EnsureAccountingPolicyAsync(
        Guid cashId,
        Guid arId,
        Guid inventoryId,
        Guid apId,
        Guid salesRevenueId,
        Guid cogsId,
        Guid inventoryAdjustmentId,
        Guid inventoryMovementsRegisterId,
        Guid itemPricesRegisterId,
        CancellationToken ct)
    {
        var page = await catalogs.GetPageAsync(
            TradeCodes.AccountingPolicy,
            new PageRequestDto(Offset: 0, Limit: 2, Search: null),
            ct);

        if (page.Items.Count > 1)
            throw new NgbConfigurationViolationException($"Multiple '{TradeCodes.AccountingPolicy}' records exist. Expected a single record.");

        var payload = Payload(new Dictionary<string, object?>
        {
            ["display"] = "Default Trade Policy",
            ["cash_account_id"] = cashId,
            ["ar_account_id"] = arId,
            ["inventory_account_id"] = inventoryId,
            ["ap_account_id"] = apId,
            ["sales_revenue_account_id"] = salesRevenueId,
            ["cogs_account_id"] = cogsId,
            ["inventory_adjustment_account_id"] = inventoryAdjustmentId,
            ["inventory_movements_register_id"] = inventoryMovementsRegisterId,
            ["item_prices_register_id"] = itemPricesRegisterId
        });

        if (page.Items.Count == 1)
        {
            await catalogs.UpdateAsync(TradeCodes.AccountingPolicy, page.Items[0].Id, payload, ct);
            return (page.Items[0].Id, false);
        }

        var created = await catalogs.CreateAsync(TradeCodes.AccountingPolicy, payload, ct);
        return (created.Id, true);
    }

    private async Task EnsureDefaultUnitOfMeasuresAsync(CancellationToken ct)
    {
        await EnsureSimpleCatalogDefaultsAsync(
            TradeCodes.UnitOfMeasure,
            [
                Payload(new Dictionary<string, object?> { ["display"] = "Each", ["name"] = "Each", ["code"] = "EA", ["symbol"] = "ea", ["is_active"] = true }),
                Payload(new Dictionary<string, object?> { ["display"] = "Box", ["name"] = "Box", ["code"] = "BOX", ["symbol"] = "box", ["is_active"] = true }),
                Payload(new Dictionary<string, object?> { ["display"] = "Pallet", ["name"] = "Pallet", ["code"] = "PAL", ["symbol"] = "plt", ["is_active"] = true })
            ],
            ct);
    }

    private async Task EnsureDefaultPaymentTermsAsync(CancellationToken ct)
    {
        await EnsureSimpleCatalogDefaultsAsync(
            TradeCodes.PaymentTerms,
            [
                Payload(new Dictionary<string, object?> { ["display"] = "Due on Receipt", ["name"] = "Due on Receipt", ["code"] = "DUE", ["due_days"] = 0, ["is_active"] = true }),
                Payload(new Dictionary<string, object?> { ["display"] = "Net 15", ["name"] = "Net 15", ["code"] = "NET15", ["due_days"] = 15, ["is_active"] = true }),
                Payload(new Dictionary<string, object?> { ["display"] = "Net 30", ["name"] = "Net 30", ["code"] = "NET30", ["due_days"] = 30, ["is_active"] = true })
            ],
            ct);
    }

    private async Task EnsureDefaultInventoryAdjustmentReasonsAsync(CancellationToken ct)
    {
        await EnsureSimpleCatalogDefaultsAsync(
            TradeCodes.InventoryAdjustmentReason,
            [
                Payload(new Dictionary<string, object?> { ["display"] = "Count Correction", ["name"] = "Count Correction", ["code"] = "COUNT", ["gl_behavior_hint"] = "Expense", ["is_active"] = true }),
                Payload(new Dictionary<string, object?> { ["display"] = "Damage", ["name"] = "Damage", ["code"] = "DAMAGE", ["gl_behavior_hint"] = "Expense", ["is_active"] = true }),
                Payload(new Dictionary<string, object?> { ["display"] = "Shrinkage", ["name"] = "Shrinkage", ["code"] = "SHRINK", ["gl_behavior_hint"] = "Expense", ["is_active"] = true })
            ],
            ct);
    }

    private async Task EnsureDefaultPriceTypesAsync(CancellationToken ct)
    {
        await EnsureSimpleCatalogDefaultsAsync(
            TradeCodes.PriceType,
            [
                Payload(new Dictionary<string, object?> { ["display"] = "Retail", ["name"] = "Retail", ["code"] = "RETAIL", ["currency"] = TradeCodes.DefaultCurrency, ["is_default"] = true, ["is_active"] = true }),
                Payload(new Dictionary<string, object?> { ["display"] = "Wholesale", ["name"] = "Wholesale", ["code"] = "WHOLESALE", ["currency"] = TradeCodes.DefaultCurrency, ["is_default"] = false, ["is_active"] = true }),
                Payload(new Dictionary<string, object?> { ["display"] = "Distributor", ["name"] = "Distributor", ["code"] = "DISTRIBUTOR", ["currency"] = TradeCodes.DefaultCurrency, ["is_default"] = false, ["is_active"] = true })
            ],
            ct);
    }

    private async Task EnsureSimpleCatalogDefaultsAsync(
        string catalogType,
        IReadOnlyList<RecordPayload> payloads,
        CancellationToken ct)
    {
        var page = await catalogs.GetPageAsync(catalogType, new PageRequestDto(Offset: 0, Limit: 200, Search: null), ct);

        foreach (var payload in payloads)
        {
            var display = payload.Fields!["display"].GetString() ?? string.Empty;
            var existing = page.Items.FirstOrDefault(x => string.Equals(x.Display, display, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                await catalogs.UpdateAsync(catalogType, existing.Id, payload, ct);
                continue;
            }

            await catalogs.CreateAsync(catalogType, payload, ct);
        }
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
            if (existing.IsDeleted)
                throw new NgbConfigurationViolationException($"Chart of Accounts account '{code}' exists but is marked for deletion.");

            if (!existing.IsActive)
                throw new NgbConfigurationViolationException($"Chart of Accounts account '{code}' exists but is inactive.");

            if (existing.Account.Type != type)
                throw new NgbConfigurationViolationException($"Chart of Accounts account '{code}' has unexpected type. Expected '{type}', actual '{existing.Account.Type}'.");

            if (existing.Account.StatementSection != statementSection)
                throw new NgbConfigurationViolationException($"Chart of Accounts account '{code}' has unexpected statement section. Expected '{statementSection}', actual '{existing.Account.StatementSection}'.");

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

            if (existing.Account.DimensionRules.Any(x => x.IsRequired))
                throw new NgbConfigurationViolationException($"Chart of Accounts account '{code}' must not require dimensions.");

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
                message: $"Chart of Accounts account '{existing.Account.Code}' exists but is incompatible with Trade defaults. " +
                         "The account has movements, so dimension rules cannot be updated automatically.",
                innerException: ex);
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
                message: $"Chart of Accounts account '{existing.Account.Code}' exists but has incompatible cash flow metadata.",
                innerException: ex);
        }
    }

    private static bool HasRequiredDimension(Account account, string dimensionCode)
    {
        var rule = account.DimensionRules.FirstOrDefault(x => string.Equals(x.DimensionCode, dimensionCode, StringComparison.OrdinalIgnoreCase));
        return rule is not null && rule.IsRequired;
    }

    private static string? NormalizeLineCode(string? lineCode)
        => string.IsNullOrWhiteSpace(lineCode) ? null : lineCode.Trim();

    private static RecordPayload Payload(IReadOnlyDictionary<string, object?> values)
    {
        var fields = values.ToDictionary(
            static x => x.Key,
            static x => JsonSerializer.SerializeToElement(x.Value),
            StringComparer.OrdinalIgnoreCase);

        return new RecordPayload(fields, null);
    }
}
