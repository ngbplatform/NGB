using Xunit;

namespace NGB.AgencyBilling.Api.IntegrationTests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class AgencyBillingPostgresCollection : ICollectionFixture<AgencyBillingPostgresFixture>
{
    public const string Name = "AgencyBillingPostgreSql";
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AgencyBillingSchemaPostgresCollection : ICollectionFixture<AgencyBillingSchemaPostgresFixture>
{
    public const string Name = "AgencyBillingPostgreSql schema changes";
}
