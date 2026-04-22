using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

/// <summary>
/// Shared PostgreSQL container for integration tests.
/// </summary>
[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresTestFixture>
{
    public const string Name = "PostgreSql";
}
