using NGB.Persistence.Migrations;
using NGB.PropertyManagement.PostgreSql.Bootstrap;

namespace NGB.PropertyManagement.PostgreSql.Migrations;

/// <summary>
/// Property Management vertical migration pack.
/// </summary>
public sealed class PropertyManagementMigrationPackContributor : IMigrationPackContributor
{
    public IEnumerable<MigrationPack> GetPacks()
    {
        yield return new MigrationPack(
            Id: "pm",
            MigrationAssemblies: [typeof(PropertyManagementMigrationPackContributor).Assembly],
            DependsOn: ["platform"],
            RepairAsync: PropertyManagementDatabaseBootstrapper.RepairModuleAsync,
            RepairWithOptionsAsync: PropertyManagementDatabaseBootstrapper.RepairModuleAsync);
    }
}
