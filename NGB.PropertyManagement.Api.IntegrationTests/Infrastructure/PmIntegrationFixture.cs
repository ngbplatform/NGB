using NGB.PostgreSql.Dapper;
using NGB.Testing.PostgreSql;

namespace NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;

public class PmIntegrationFixture : PostgreSqlIntegrationFixtureBase
{
    private PmKeycloakFixture? _keycloak;

    public PmIntegrationFixture()
    {
        DapperTypeHandlers.Register();
    }

    protected override string DatabaseName => "ngb_pm_tests";

    public PmKeycloakFixture Keycloak => _keycloak
        ?? throw new NotSupportedException("Keycloak fixture is not initialized.");

    protected override Task ApplyMigrationsAsync(
        string connectionString,
        CancellationToken cancellationToken) =>
        PmMigrationSet.ApplyPlatformAndPmMigrationsAsync(connectionString, cancellationToken);

    protected override Task InitializeAuxiliaryResourcesAsync()
    {
        _keycloak = new PmKeycloakFixture();
        return _keycloak.InitializeAsync();
    }

    protected override async ValueTask DisposeAuxiliaryResourcesAsync()
    {
        if (_keycloak is not null)
        {
            await _keycloak.DisposeAsync();
        }
    }
}

public sealed class PmSchemaIntegrationFixture : PmIntegrationFixture
{
    protected override bool RebuildSchemaBeforeReset => true;
}
