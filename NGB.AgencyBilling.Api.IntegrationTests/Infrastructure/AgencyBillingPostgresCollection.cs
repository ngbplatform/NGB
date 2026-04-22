using Xunit;

namespace NGB.AgencyBilling.Api.IntegrationTests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class AgencyBillingPostgresCollection : ICollectionFixture<AgencyBillingPostgresFixture>
{
    public const string Name = "AgencyBillingPostgreSql";
}
