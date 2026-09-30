using NGB.PostgreSql.Dapper;
using NGB.Testing.PostgreSql;

namespace NGB.CRM.Api.IntegrationTests.Infrastructure;

public class CrmPostgresFixture : PostgreSqlIntegrationFixtureBase
{
    public CrmPostgresFixture()
    {
        DapperTypeHandlers.Register();
    }

    protected override string DatabaseName => "ngb_crm_tests";

    protected override Task ApplyMigrationsAsync(
        string connectionString,
        CancellationToken cancellationToken) =>
        CrmMigrationSet.ApplyPlatformAndCrmMigrationsAsync(connectionString, cancellationToken);
}

public sealed class CrmSchemaPostgresFixture : CrmPostgresFixture
{
}
