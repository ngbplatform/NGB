using NGB.Persistence.Migrations;

namespace EquipmentInspection.PostgreSql;

public sealed class EquipmentMigrationPack : IMigrationPackContributor
{
    public IEnumerable<MigrationPack> GetPacks()
    {
        yield return new MigrationPack(
            Id: "equipment-inspection",
            MigrationAssemblies: [typeof(EquipmentMigrationPack).Assembly],
            DependsOn: ["platform"]);
    }
}
