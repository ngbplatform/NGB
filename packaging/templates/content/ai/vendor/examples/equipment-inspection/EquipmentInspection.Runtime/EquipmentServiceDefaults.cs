using EquipmentInspection.Definitions;
using NGB.Accounting.Accounts;
using NGB.Accounting.Dimensions;
using NGB.OperationalRegisters.Contracts;
using NGB.Runtime.Accounts;
using NGB.Runtime.OperationalRegisters;

namespace EquipmentInspection.Runtime;

public sealed class EquipmentServiceDefaults(
    IChartOfAccountsAdminService chart,
    IChartOfAccountsManagementService accounts,
    IOperationalRegisterManagementService registers,
    IOperationalRegisterAdminMaintenanceService maintenance)
{
    public async Task ApplyAsync(CancellationToken ct)
    {
        var existing = await chart.GetAsync(includeDeleted: true, ct);
        await EnsureAccountAsync(existing, EquipmentServiceDefinitions.ExpenseAccountCode, "Equipment service expense",
            AccountType.Expense, [new AccountDimensionRule(EquipmentDefinitions.EquipmentDimensionId,
                EquipmentDefinitions.EquipmentCode, 1, true)], ct);
        await EnsureAccountAsync(existing, EquipmentServiceDefinitions.PayableAccountCode, "Equipment service payable",
            AccountType.Liability, [], ct);

        var id = await registers.UpsertAsync(EquipmentServiceDefinitions.RegisterCode, "Equipment service hours", ct);
        await registers.ReplaceResourcesAsync(id, [new OperationalRegisterResourceDefinition("hours", "Hours", 1)], ct);
        await registers.ReplaceDimensionRulesAsync(id,
            [new OperationalRegisterDimensionRule(EquipmentDefinitions.EquipmentDimensionId, EquipmentDefinitions.EquipmentCode, 1, true)], ct);
        await maintenance.EnsurePhysicalSchemaByIdAsync(id, ct);
    }

    private async Task EnsureAccountAsync(
        IReadOnlyList<ChartOfAccountsAdminItem> existing,
        string code,
        string name,
        AccountType type,
        IReadOnlyList<AccountDimensionRule> rules,
        CancellationToken ct)
    {
        var current = existing.SingleOrDefault(item => AccountCode.Normalize(item.Account.Code) == AccountCode.Normalize(code));
        if (current is null)
        {
            await accounts.CreateAsync(new CreateAccountRequest(code, name, type,
                DimensionRules: rules.Select(rule => new AccountDimensionRuleRequest(rule.DimensionCode, rule.IsRequired, rule.Ordinal)).ToArray()), ct);
            return;
        }

        var expected = new Account(current.Account.Id, code, name, type, dimensionRules: rules);
        var actual = current.Account;
        if (current.IsDeleted || !current.IsActive
            || (actual.Type, actual.StatementSection, actual.IsContra, actual.NegativeBalancePolicy)
                != (expected.Type, expected.StatementSection, expected.IsContra, expected.NegativeBalancePolicy)
            || !actual.DimensionRules.SequenceEqual(expected.DimensionRules))
        {
            throw new InvalidOperationException($"Account '{code}' is incompatible with the equipment service defaults.");
        }
    }
}
