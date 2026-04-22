using NGB.PostgreSql.Bootstrap;
using NGB.PostgreSql.Migrations.Evolve;
using NGB.Trade.PostgreSql.Migrations;

namespace NGB.Trade.Api.IntegrationTests.Infrastructure;

internal static class TradeMigrationSet
{
    public static Task ApplyPlatformAndTradeMigrationsAsync(string connectionString, CancellationToken ct = default)
    {
        var packs = SchemaMigrator.DiscoverPacks(
        [
            typeof(DatabaseBootstrapper).Assembly,
            typeof(TradeMigrationPackContributor).Assembly
        ]);

        return SchemaMigrator.MigrateAsync(
            connectionString,
            packs,
            includePackIds: ["trade"],
            repair: true,
            dryRun: false,
            log: null,
            ct: ct);
    }
}
