using NGB.CRM.PostgreSql.Bootstrap;
using NGB.CRM.Migrator.Seed;
using NGB.Migrator.Core;

namespace NGB.CRM.Migrator;

internal static class Program
{
    public static Task<int> Main(string[] args)
    {
        _ = typeof(CrmDatabaseBootstrapper).Assembly;

        if (CrmSeedDefaultsCli.IsSeedDefaultsCommand(args))
            return CrmSeedDefaultsCli.RunAsync(CrmSeedDefaultsCli.TrimCommand(args));

        if (CrmSeedDemoCli.IsSeedDemoCommand(args))
            return CrmSeedDemoCli.RunAsync(CrmSeedDemoCli.TrimCommand(args));

        return PlatformMigratorCli.RunAsync(args);
    }
}
