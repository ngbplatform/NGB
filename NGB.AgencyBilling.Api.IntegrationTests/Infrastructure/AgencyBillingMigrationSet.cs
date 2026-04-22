using NGB.AgencyBilling.PostgreSql.Migrations;
using NGB.PostgreSql.Bootstrap;
using NGB.PostgreSql.Migrations.Evolve;

namespace NGB.AgencyBilling.Api.IntegrationTests.Infrastructure;

internal static class AgencyBillingMigrationSet
{
    public static Task ApplyPlatformAndAgencyBillingMigrationsAsync(
        string connectionString,
        CancellationToken ct = default)
    {
        var packs = SchemaMigrator.DiscoverPacks(
        [
            typeof(DatabaseBootstrapper).Assembly,
            typeof(AgencyBillingMigrationPackContributor).Assembly
        ]);

        return SchemaMigrator.MigrateAsync(
            connectionString,
            packs,
            includePackIds: ["agency-billing"],
            repair: true,
            dryRun: false,
            log: null,
            ct: ct);
    }
}
