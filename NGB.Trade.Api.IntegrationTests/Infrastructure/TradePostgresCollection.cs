using Xunit;

namespace NGB.Trade.Api.IntegrationTests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class TradePostgresCollection : ICollectionFixture<TradePostgresFixture>
{
    public const string Name = "TradePostgreSql";
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TradeSchemaPostgresCollection : ICollectionFixture<TradeSchemaPostgresFixture>
{
    public const string Name = "TradePostgreSql schema changes";
}
