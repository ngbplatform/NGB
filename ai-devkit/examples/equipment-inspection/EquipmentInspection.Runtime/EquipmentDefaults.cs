using EquipmentInspection.Definitions;
using NGB.OperationalRegisters.Contracts;
using NGB.Runtime.OperationalRegisters;

namespace EquipmentInspection.Runtime;

public sealed class EquipmentDefaults(
    IOperationalRegisterManagementService registers,
    IOperationalRegisterAdminMaintenanceService maintenance)
{
    public async Task ApplyAsync(CancellationToken ct)
    {
        var id = await registers.UpsertAsync(EquipmentUsageDefinitions.RegisterCode, "Equipment operating hours", ct);
        await registers.ReplaceResourcesAsync(id, [new OperationalRegisterResourceDefinition("hours", "Hours", 1)], ct);
        await registers.ReplaceDimensionRulesAsync(id,
            [new OperationalRegisterDimensionRule(EquipmentDefinitions.EquipmentDimensionId, "example.equipment", 1, true)], ct);
        await maintenance.EnsurePhysicalSchemaByIdAsync(id, ct);
    }
}
