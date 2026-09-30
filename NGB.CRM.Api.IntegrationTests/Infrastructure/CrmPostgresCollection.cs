using Xunit;

namespace NGB.CRM.Api.IntegrationTests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class CrmPostgresCollection : ICollectionFixture<CrmPostgresFixture>
{
    public const string Name = "CRM PostgreSQL";
}

[CollectionDefinition(Name)]
public sealed class CrmDocumentsPostgresCollection : ICollectionFixture<CrmPostgresFixture>
{
    public const string Name = "CRM PostgreSQL documents";
}

[CollectionDefinition(Name)]
public sealed class CrmSeedPostgresCollection : ICollectionFixture<CrmPostgresFixture>
{
    public const string Name = "CRM PostgreSQL seed verification";
}

[CollectionDefinition(Name)]
public sealed class CrmSeededReportingCollection : ICollectionFixture<CrmSeededReportingFixture>
{
    public const string Name = "CRM PostgreSQL seeded reporting";
}

[CollectionDefinition(Name)]
public sealed class CrmSchemaPostgresCollection : ICollectionFixture<CrmSchemaPostgresFixture>
{
    public const string Name = "CRM PostgreSQL schema changes";
}
