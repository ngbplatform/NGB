using System.Text.Json;
using NGB.Accounting.Accounts;
using NGB.Accounting.CashFlow;
using NGB.AgencyBilling.Contracts;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.OperationalRegisters;
using NGB.Runtime.Accounts;
using NGB.Runtime.Accounts.Exceptions;
using NGB.Runtime.OperationalRegisters;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.AgencyBilling.Runtime;

public sealed class AgencyBillingSetupService(
    IChartOfAccountsAdminService coaAdmin,
    IChartOfAccountsManagementService coaManagement,
    IOperationalRegisterManagementService opregManagement,
    IOperationalRegisterRepository opregRepo,
    IOperationalRegisterAdminMaintenanceService opregMaintenance,
    ICatalogService catalogs)
    : IAgencyBillingSetupService
{
    private const string CashCode = "1000";
    private const string AccountsReceivableCode = "1100";
    private const string ServiceRevenueCode = "4000";

    private static readonly string[] ReceivableDimensions = [AgencyBillingCodes.Client, AgencyBillingCodes.Project];
    private static readonly string[] RevenueDimensions = [AgencyBillingCodes.Client, AgencyBillingCodes.Project];

    public async Task<AgencyBillingSetupResult> EnsureDefaultsAsync(CancellationToken ct = default)
    {
        var coa = await coaAdmin.GetAsync(includeDeleted: true, ct);

        var (cashId, createdCash) = await EnsureCashAccountAsync(coa, CashCode, "Operating Bank", ct);
        var (arId, createdAr) = await EnsureAccountAsync(
            coa,
            AccountsReceivableCode,
            "Accounts Receivable",
            AccountType.Asset,
            StatementSection.Assets,
            ReceivableDimensions,
            ct,
            CashFlowRole.WorkingCapital,
            CashFlowSystemLineCodes.WorkingCapitalAccountsReceivable);
        var (serviceRevenueId, createdServiceRevenue) = await EnsureAccountAsync(
            coa,
            ServiceRevenueCode,
            "Service Revenue",
            AccountType.Income,
            StatementSection.Income,
            RevenueDimensions,
            ct);

        var (projectTimeLedgerId, createdProjectTimeLedger) = await EnsureProjectTimeLedgerOperationalRegisterAsync(ct);
        var (unbilledTimeId, createdUnbilledTime) = await EnsureUnbilledTimeOperationalRegisterAsync(ct);
        var (projectBillingStatusId, createdProjectBillingStatus) = await EnsureProjectBillingStatusOperationalRegisterAsync(ct);
        var (arOpenItemsId, createdArOpenItems) = await EnsureArOpenItemsOperationalRegisterAsync(ct);
        var (policyId, createdPolicy) = await EnsureAccountingPolicyAsync(
            cashId,
            arId,
            serviceRevenueId,
            projectTimeLedgerId,
            unbilledTimeId,
            projectBillingStatusId,
            arOpenItemsId,
            ct);

        await EnsureDefaultPaymentTermsAsync(ct);

        return new AgencyBillingSetupResult(
            CashAccountId: cashId,
            AccountsReceivableAccountId: arId,
            ServiceRevenueAccountId: serviceRevenueId,
            ProjectTimeLedgerOperationalRegisterId: projectTimeLedgerId,
            UnbilledTimeOperationalRegisterId: unbilledTimeId,
            ProjectBillingStatusOperationalRegisterId: projectBillingStatusId,
            ArOpenItemsOperationalRegisterId: arOpenItemsId,
            AccountingPolicyCatalogId: policyId,
            CreatedCashAccount: createdCash,
            CreatedAccountsReceivableAccount: createdAr,
            CreatedServiceRevenueAccount: createdServiceRevenue,
            CreatedProjectTimeLedgerOperationalRegister: createdProjectTimeLedger,
            CreatedUnbilledTimeOperationalRegister: createdUnbilledTime,
            CreatedProjectBillingStatusOperationalRegister: createdProjectBillingStatus,
            CreatedArOpenItemsOperationalRegister: createdArOpenItems,
            CreatedAccountingPolicy: createdPolicy);
    }

    private async Task<(Guid Id, bool Created)> EnsureProjectTimeLedgerOperationalRegisterAsync(CancellationToken ct)
    {
        var existed = await opregRepo.GetByCodeAsync(AgencyBillingCodes.ProjectTimeLedgerRegisterCode, ct);
        var id = await opregManagement.UpsertAsync(AgencyBillingCodes.ProjectTimeLedgerRegisterCode, "Project Time Ledger", ct);

        await opregManagement.ReplaceResourcesAsync(
            id,
            [
                new OperationalRegisterResourceDefinition("hours_total", "Hours Total", 1),
                new OperationalRegisterResourceDefinition("billable_hours", "Billable Hours", 2),
                new OperationalRegisterResourceDefinition("non_billable_hours", "Non-Billable Hours", 3),
                new OperationalRegisterResourceDefinition("billable_amount", "Billable Amount", 4),
                new OperationalRegisterResourceDefinition("cost_amount", "Cost Amount", 5)
            ],
            ct);

        await opregManagement.ReplaceDimensionRulesAsync(
            id,
            [
                new OperationalRegisterDimensionRule(
                    DimensionId: DeterministicGuid.Create($"Dimension|{AgencyBillingCodes.Client}"),
                    DimensionCode: AgencyBillingCodes.Client,
                    Ordinal: 1,
                    IsRequired: true),
                new OperationalRegisterDimensionRule(
                    DimensionId: DeterministicGuid.Create($"Dimension|{AgencyBillingCodes.Project}"),
                    DimensionCode: AgencyBillingCodes.Project,
                    Ordinal: 2,
                    IsRequired: true),
                new OperationalRegisterDimensionRule(
                    DimensionId: DeterministicGuid.Create($"Dimension|{AgencyBillingCodes.TeamMember}"),
                    DimensionCode: AgencyBillingCodes.TeamMember,
                    Ordinal: 3,
                    IsRequired: true),
                new OperationalRegisterDimensionRule(
                    DimensionId: DeterministicGuid.Create($"Dimension|{AgencyBillingCodes.ServiceItem}"),
                    DimensionCode: AgencyBillingCodes.ServiceItem,
                    Ordinal: 4,
                    IsRequired: false)
            ],
            ct);

        await opregMaintenance.EnsurePhysicalSchemaByIdAsync(id, ct);
        return (id, existed is null);
    }

    private async Task<(Guid Id, bool Created)> EnsureUnbilledTimeOperationalRegisterAsync(CancellationToken ct)
    {
        var existed = await opregRepo.GetByCodeAsync(AgencyBillingCodes.UnbilledTimeRegisterCode, ct);
        var id = await opregManagement.UpsertAsync(AgencyBillingCodes.UnbilledTimeRegisterCode, "Unbilled Time", ct);

        await opregManagement.ReplaceResourcesAsync(
            id,
            [
                new OperationalRegisterResourceDefinition("hours_open", "Hours Open", 1),
                new OperationalRegisterResourceDefinition("amount_open", "Amount Open", 2)
            ],
            ct);

        await opregManagement.ReplaceDimensionRulesAsync(
            id,
            [
                new OperationalRegisterDimensionRule(
                    DimensionId: DeterministicGuid.Create($"Dimension|{AgencyBillingCodes.Client}"),
                    DimensionCode: AgencyBillingCodes.Client,
                    Ordinal: 1,
                    IsRequired: true),
                new OperationalRegisterDimensionRule(
                    DimensionId: DeterministicGuid.Create($"Dimension|{AgencyBillingCodes.Project}"),
                    DimensionCode: AgencyBillingCodes.Project,
                    Ordinal: 2,
                    IsRequired: true),
                new OperationalRegisterDimensionRule(
                    DimensionId: DeterministicGuid.Create($"Dimension|{AgencyBillingCodes.TeamMember}"),
                    DimensionCode: AgencyBillingCodes.TeamMember,
                    Ordinal: 3,
                    IsRequired: true),
                new OperationalRegisterDimensionRule(
                    DimensionId: DeterministicGuid.Create($"Dimension|{AgencyBillingCodes.ServiceItem}"),
                    DimensionCode: AgencyBillingCodes.ServiceItem,
                    Ordinal: 4,
                    IsRequired: false)
            ],
            ct);

        await opregMaintenance.EnsurePhysicalSchemaByIdAsync(id, ct);
        return (id, existed is null);
    }

    private async Task<(Guid Id, bool Created)> EnsureProjectBillingStatusOperationalRegisterAsync(CancellationToken ct)
    {
        var existed = await opregRepo.GetByCodeAsync(AgencyBillingCodes.ProjectBillingStatusRegisterCode, ct);
        var id = await opregManagement.UpsertAsync(AgencyBillingCodes.ProjectBillingStatusRegisterCode, "Project Billing Status", ct);

        await opregManagement.ReplaceResourcesAsync(
            id,
            [
                new OperationalRegisterResourceDefinition("billed_amount", "Billed Amount", 1),
                new OperationalRegisterResourceDefinition("collected_amount", "Collected Amount", 2),
                new OperationalRegisterResourceDefinition("outstanding_ar_amount", "Outstanding AR Amount", 3)
            ],
            ct);

        await opregManagement.ReplaceDimensionRulesAsync(
            id,
            [
                new OperationalRegisterDimensionRule(
                    DimensionId: DeterministicGuid.Create($"Dimension|{AgencyBillingCodes.Client}"),
                    DimensionCode: AgencyBillingCodes.Client,
                    Ordinal: 1,
                    IsRequired: true),
                new OperationalRegisterDimensionRule(
                    DimensionId: DeterministicGuid.Create($"Dimension|{AgencyBillingCodes.Project}"),
                    DimensionCode: AgencyBillingCodes.Project,
                    Ordinal: 2,
                    IsRequired: true)
            ],
            ct);

        await opregMaintenance.EnsurePhysicalSchemaByIdAsync(id, ct);
        return (id, existed is null);
    }

    private async Task<(Guid Id, bool Created)> EnsureArOpenItemsOperationalRegisterAsync(CancellationToken ct)
    {
        var existed = await opregRepo.GetByCodeAsync(AgencyBillingCodes.ArOpenItemsRegisterCode, ct);
        var id = await opregManagement.UpsertAsync(AgencyBillingCodes.ArOpenItemsRegisterCode, "AR Open Items", ct);

        await opregManagement.ReplaceResourcesAsync(
            id,
            [
                new OperationalRegisterResourceDefinition("amount", "Amount", 1)
            ],
            ct);

        await opregManagement.ReplaceDimensionRulesAsync(
            id,
            [
                new OperationalRegisterDimensionRule(
                    DimensionId: DeterministicGuid.Create($"Dimension|{AgencyBillingCodes.Client}"),
                    DimensionCode: AgencyBillingCodes.Client,
                    Ordinal: 1,
                    IsRequired: true),
                new OperationalRegisterDimensionRule(
                    DimensionId: DeterministicGuid.Create($"Dimension|{AgencyBillingCodes.Project}"),
                    DimensionCode: AgencyBillingCodes.Project,
                    Ordinal: 2,
                    IsRequired: true),
                new OperationalRegisterDimensionRule(
                    DimensionId: DeterministicGuid.Create($"Dimension|{AgencyBillingCodes.ArOpenItemDimensionCode}"),
                    DimensionCode: AgencyBillingCodes.ArOpenItemDimensionCode,
                    Ordinal: 3,
                    IsRequired: true)
            ],
            ct);

        await opregMaintenance.EnsurePhysicalSchemaByIdAsync(id, ct);
        return (id, existed is null);
    }

    private async Task<(Guid Id, bool Created)> EnsureAccountingPolicyAsync(
        Guid cashId,
        Guid arId,
        Guid serviceRevenueId,
        Guid projectTimeLedgerId,
        Guid unbilledTimeId,
        Guid projectBillingStatusId,
        Guid arOpenItemsId,
        CancellationToken ct)
    {
        var page = await catalogs.GetPageAsync(
            AgencyBillingCodes.AccountingPolicy,
            new PageRequestDto(Offset: 0, Limit: 2, Search: null),
            ct);

        if (page.Items.Count > 1)
            throw new NgbConfigurationViolationException($"Multiple '{AgencyBillingCodes.AccountingPolicy}' records exist. Expected a single record.");

        var payload = Payload(new Dictionary<string, object?>
        {
            ["display"] = "Default Agency Billing Policy",
            ["cash_account_id"] = cashId,
            ["ar_account_id"] = arId,
            ["service_revenue_account_id"] = serviceRevenueId,
            ["project_time_ledger_register_id"] = projectTimeLedgerId,
            ["unbilled_time_register_id"] = unbilledTimeId,
            ["project_billing_status_register_id"] = projectBillingStatusId,
            ["ar_open_items_register_id"] = arOpenItemsId,
            ["default_currency"] = AgencyBillingCodes.DefaultCurrency
        });

        if (page.Items.Count == 1)
        {
            await catalogs.UpdateAsync(AgencyBillingCodes.AccountingPolicy, page.Items[0].Id, payload, ct);
            return (page.Items[0].Id, false);
        }

        var created = await catalogs.CreateAsync(AgencyBillingCodes.AccountingPolicy, payload, ct);
        return (created.Id, true);
    }

    private async Task EnsureDefaultPaymentTermsAsync(CancellationToken ct)
    {
        await EnsureSimpleCatalogDefaultsAsync(
            AgencyBillingCodes.PaymentTerms,
            [
                Payload(new Dictionary<string, object?> { ["display"] = "Due on Receipt", ["name"] = "Due on Receipt", ["code"] = "DUE", ["due_days"] = 0, ["is_active"] = true }),
                Payload(new Dictionary<string, object?> { ["display"] = "Net 15", ["name"] = "Net 15", ["code"] = "NET15", ["due_days"] = 15, ["is_active"] = true }),
                Payload(new Dictionary<string, object?> { ["display"] = "Net 30", ["name"] = "Net 30", ["code"] = "NET30", ["due_days"] = 30, ["is_active"] = true })
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
                message: $"Chart of Accounts account '{existing.Account.Code}' exists but is incompatible with Agency Billing defaults. " +
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

        if (actualRole == expectedRole
            && string.Equals(actualLineCode, normalizedExpectedLineCode, StringComparison.Ordinal))
        {
            return;
        }

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

        return new RecordPayload(fields);
    }
}
