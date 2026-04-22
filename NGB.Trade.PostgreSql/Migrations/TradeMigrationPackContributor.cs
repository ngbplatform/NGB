using NGB.Persistence.Migrations;
using NGB.Trade.PostgreSql.Bootstrap;

namespace NGB.Trade.PostgreSql.Migrations;

/// <summary>
/// Trade vertical migration pack.
/// </summary>
public sealed class TradeMigrationPackContributor : IMigrationPackContributor
{
    public IEnumerable<MigrationPack> GetPacks()
    {
        yield return new MigrationPack(
            Id: "trade",
            MigrationAssemblies: [typeof(TradeMigrationPackContributor).Assembly],
            DependsOn: ["platform"],
            RepairAsync: TradeDatabaseBootstrapper.RepairModuleAsync,
            RepairWithOptionsAsync: TradeDatabaseBootstrapper.RepairModuleAsync);
    }
}
