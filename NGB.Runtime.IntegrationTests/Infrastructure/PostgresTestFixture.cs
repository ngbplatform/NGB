using NGB.PostgreSql.Dapper;
using NGB.Testing.PostgreSql;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

public class PostgresTestFixture : PostgreSqlIntegrationFixtureBase
{
    public PostgresTestFixture()
    {
        DapperTypeHandlers.Register();
    }

    protected override string DatabaseName => "ngb_tests";

    protected override Task ApplyMigrationsAsync(
        string connectionString,
        CancellationToken cancellationToken) =>
        MigrationSet.ApplyPlatformMigrationsAsync(connectionString, cancellationToken);
}

public sealed class SchemaPostgresTestFixture : PostgresTestFixture
{
    protected override bool RebuildSchemaBeforeReset => true;
}
