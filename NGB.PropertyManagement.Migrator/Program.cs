using NGB.Migrator.Core;
using NGB.PropertyManagement.Migrator.Seed;
using NGB.PropertyManagement.PostgreSql.Bootstrap;

namespace NGB.PropertyManagement.Migrator;

internal static class Program
{
    public static Task<int> Main(string[] args)
    {
        // Force-load the PM PostgreSQL module assembly so migration pack discovery is deterministic.
        // This also surfaces missing dependency issues early instead of silently skipping the pack.
        _ = typeof(PropertyManagementDatabaseBootstrapper).Assembly;

        if (PropertyManagementSeedDefaultsCli.IsSeedDefaultsCommand(args))
            return PropertyManagementSeedDefaultsCli.RunAsync(PropertyManagementSeedDefaultsCli.TrimCommand(args));

        if (PropertyManagementSeedDemoCli.IsSeedDemoCommand(args))
            return PropertyManagementSeedDemoCli.RunAsync(PropertyManagementSeedDemoCli.TrimCommand(args));

        return PlatformMigratorCli.RunAsync(args);
    }
}
