using NGB.Migrator.Core;
using NGB.Trade.Migrator.Seed;
using NGB.Trade.PostgreSql.Bootstrap;

namespace NGB.Trade.Migrator;

internal static class Program
{
    public static Task<int> Main(string[] args)
    {
        _ = typeof(TradeDatabaseBootstrapper).Assembly;

        if (TradeSeedDefaultsCli.IsSeedDefaultsCommand(args))
            return TradeSeedDefaultsCli.RunAsync(TradeSeedDefaultsCli.TrimCommand(args));

        if (TradeSeedDemoCli.IsSeedDemoCommand(args))
            return TradeSeedDemoCli.RunAsync(TradeSeedDemoCli.TrimCommand(args));

        return PlatformMigratorCli.RunAsync(args);
    }
}
