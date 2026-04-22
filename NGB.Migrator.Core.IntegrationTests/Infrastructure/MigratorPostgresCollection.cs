using Xunit;

namespace NGB.Migrator.Core.IntegrationTests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class MigratorPostgresCollection : ICollectionFixture<MigratorPostgresFixture>
{
    public const string Name = "migrator-postgres";
}
