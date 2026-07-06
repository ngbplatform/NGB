using NGB.CRM.PostgreSql.Migrations;
using NGB.PostgreSql.Bootstrap;
using NGB.PostgreSql.Migrations.Evolve;

namespace NGB.CRM.Api.IntegrationTests.Infrastructure;

internal static class CrmMigrationSet
{
    public static Task ApplyPlatformAndCrmMigrationsAsync(string connectionString, CancellationToken ct = default)
    {
        var packs = SchemaMigrator.DiscoverPacks(
        [
            typeof(DatabaseBootstrapper).Assembly,
            typeof(CrmMigrationPackContributor).Assembly
        ]);

        return SchemaMigrator.MigrateAsync(
            connectionString,
            packs,
            includePackIds: ["crm"],
            repair: true,
            dryRun: false,
            log: null,
            ct: ct);
    }
}
