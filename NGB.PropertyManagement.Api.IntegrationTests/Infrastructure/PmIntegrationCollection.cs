using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class PmIntegrationCollection : ICollectionFixture<PmIntegrationFixture>
{
    public const string Name = "pm-integration";
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PmSchemaIntegrationCollection : ICollectionFixture<PmSchemaIntegrationFixture>
{
    public const string Name = "pm-integration-schema-changes";
}
