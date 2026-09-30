using NGB.PostgreSql.Dapper;
using NGB.Testing.PostgreSql;

namespace NGB.AgencyBilling.Api.IntegrationTests.Infrastructure;

public class AgencyBillingPostgresFixture : PostgreSqlIntegrationFixtureBase
{
    public AgencyBillingPostgresFixture()
    {
        DapperTypeHandlers.Register();
    }

    protected override string DatabaseName => "ngb_agency_billing_tests";

    protected override Task ApplyMigrationsAsync(
        string connectionString,
        CancellationToken cancellationToken) =>
        AgencyBillingMigrationSet.ApplyPlatformAndAgencyBillingMigrationsAsync(
            connectionString,
            cancellationToken);
}

public sealed class AgencyBillingSchemaPostgresFixture : AgencyBillingPostgresFixture
{
    protected override bool RebuildSchemaBeforeReset => true;
}
