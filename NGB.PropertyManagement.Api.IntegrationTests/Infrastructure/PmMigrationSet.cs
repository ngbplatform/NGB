using NGB.PostgreSql.Bootstrap;
using NGB.PostgreSql.Migrations.Evolve;
using NGB.PropertyManagement.PostgreSql.Migrations;

namespace NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;

internal static class PmMigrationSet
{
    /// <summary>
    /// Applies the same platform + PM migration plan used by the real migrator host.
    /// IMPORTANT: include only "pm" here so dependency resolution must also bring in "platform".
    /// </summary>
    public static Task ApplyPlatformAndPmMigrationsAsync(string connectionString, CancellationToken ct = default)
    {
        var packs = SchemaMigrator.DiscoverPacks(
        [
            typeof(DatabaseBootstrapper).Assembly,
            typeof(PropertyManagementMigrationPackContributor).Assembly
        ]);

        return SchemaMigrator.MigrateAsync(
            connectionString,
            packs,
            includePackIds: ["pm"],
            repair: true,
            dryRun: false,
            log: null,
            ct: ct);
    }
}
