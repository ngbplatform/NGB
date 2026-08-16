using NGB.PostgreSql.Dapper;
using NGB.Testing.PostgreSql;

namespace NGB.Trade.Api.IntegrationTests.Infrastructure;

public class TradePostgresFixture : PostgreSqlIntegrationFixtureBase
{
    public TradePostgresFixture()
    {
        DapperTypeHandlers.Register();
    }

    protected override string DatabaseName => "ngb_trade_tests";

    protected override Task ApplyMigrationsAsync(
        string connectionString,
        CancellationToken cancellationToken) =>
        TradeMigrationSet.ApplyPlatformAndTradeMigrationsAsync(connectionString, cancellationToken);
}

public sealed class TradeSchemaPostgresFixture : TradePostgresFixture
{
    protected override bool RebuildSchemaBeforeReset => true;
}
