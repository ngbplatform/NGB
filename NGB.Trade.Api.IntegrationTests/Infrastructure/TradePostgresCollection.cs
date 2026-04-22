using Xunit;

namespace NGB.Trade.Api.IntegrationTests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class TradePostgresCollection : ICollectionFixture<TradePostgresFixture>
{
    public const string Name = "TradePostgreSql";
}
