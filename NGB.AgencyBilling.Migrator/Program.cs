using NGB.AgencyBilling.Migrator.Seed;
using NGB.AgencyBilling.PostgreSql.Bootstrap;
using NGB.Migrator.Core;

namespace NGB.AgencyBilling.Migrator;

internal static class Program
{
    public static Task<int> Main(string[] args)
    {
        _ = typeof(AgencyBillingDatabaseBootstrapper).Assembly;

        if (AgencyBillingSeedDefaultsCli.IsSeedDefaultsCommand(args))
            return AgencyBillingSeedDefaultsCli.RunAsync(AgencyBillingSeedDefaultsCli.TrimCommand(args));

        if (AgencyBillingSeedDemoCli.IsSeedDemoCommand(args))
            return AgencyBillingSeedDemoCli.RunAsync(AgencyBillingSeedDemoCli.TrimCommand(args));

        return PlatformMigratorCli.RunAsync(args);
    }
}
