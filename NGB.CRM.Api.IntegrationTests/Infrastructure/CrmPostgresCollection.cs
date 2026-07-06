using Xunit;

namespace NGB.CRM.Api.IntegrationTests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class CrmPostgresCollection : ICollectionFixture<CrmPostgresFixture>
{
    public const string Name = "CRM PostgreSQL";
}
