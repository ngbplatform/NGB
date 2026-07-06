using NGB.CRM.PostgreSql.Bootstrap;
using NGB.Persistence.Migrations;

namespace NGB.CRM.PostgreSql.Migrations;

public sealed class CrmMigrationPackContributor : IMigrationPackContributor
{
    public IEnumerable<MigrationPack> GetPacks()
    {
        yield return new MigrationPack(
            Id: "crm",
            MigrationAssemblies: [typeof(CrmMigrationPackContributor).Assembly],
            DependsOn: ["platform"],
            RepairAsync: CrmDatabaseBootstrapper.RepairModuleAsync,
            RepairWithOptionsAsync: CrmDatabaseBootstrapper.RepairModuleAsync);
    }
}
