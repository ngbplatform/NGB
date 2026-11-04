using EquipmentInspection.Definitions;
using NGB.Metadata.Base;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.Runtime.ReferenceRegisters;

namespace EquipmentInspection.Runtime;

public sealed class EquipmentConditionDefaults(
    IReferenceRegisterManagementService registers,
    IReferenceRegisterAdminMaintenanceService maintenance)
{
    public async Task ApplyAsync(CancellationToken ct)
    {
        var id = await registers.UpsertAsync(EquipmentConditionDefinitions.RegisterCode, "Equipment assessment records",
            ReferenceRegisterPeriodicity.NonPeriodic, ReferenceRegisterRecordMode.SubordinateToRecorder, ct);
        await registers.ReplaceFieldsAsync(id,
            [new ReferenceRegisterFieldDefinition("condition", "Condition", 1, ColumnType.String, false)], ct);
        await registers.ReplaceDimensionRulesAsync(id,
            [new ReferenceRegisterDimensionRule(EquipmentDefinitions.EquipmentDimensionId, "example.equipment", 1, true)], ct);
        await maintenance.EnsurePhysicalSchemaByIdAsync(id, ct);
    }
}
