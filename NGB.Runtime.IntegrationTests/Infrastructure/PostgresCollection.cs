using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class DocumentsPostgresCollection : ICollectionFixture<PostgresTestFixture>
{
    public const string Name = "PostgreSQL documents";
}

[CollectionDefinition(Name)]
public sealed class AccountingPostgresCollection : ICollectionFixture<PostgresTestFixture>
{
    public const string Name = "PostgreSQL accounting";
}

[CollectionDefinition(Name)]
public sealed class RegistersPostgresCollection : ICollectionFixture<PostgresTestFixture>
{
    public const string Name = "PostgreSQL registers";
}

[CollectionDefinition(Name)]
public sealed class PlatformPostgresCollection : ICollectionFixture<PostgresTestFixture>
{
    public const string Name = "PostgreSQL platform";
}

[CollectionDefinition(Name)]
public sealed class SchemaPostgresCollection : ICollectionFixture<SchemaPostgresTestFixture>
{
    public const string Name = "PostgreSQL schema changes";
}
