using NGB.AgencyBilling.PostgreSql.Bootstrap;
using NGB.Persistence.Migrations;

namespace NGB.AgencyBilling.PostgreSql.Migrations;

/// <summary>
/// Agency Billing vertical migration pack.
/// </summary>
public sealed class AgencyBillingMigrationPackContributor : IMigrationPackContributor
{
    public IEnumerable<MigrationPack> GetPacks()
    {
        yield return new MigrationPack(
            Id: "agency-billing",
            MigrationAssemblies: [typeof(AgencyBillingMigrationPackContributor).Assembly],
            DependsOn: ["platform"],
            RepairAsync: AgencyBillingDatabaseBootstrapper.RepairModuleAsync,
            RepairWithOptionsAsync: AgencyBillingDatabaseBootstrapper.RepairModuleAsync);
    }
}
