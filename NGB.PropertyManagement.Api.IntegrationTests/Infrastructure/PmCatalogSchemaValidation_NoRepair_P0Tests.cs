using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.PostgreSql.Bootstrap;
using NGB.PostgreSql.Migrations.Evolve;
using NGB.PropertyManagement.PostgreSql.Migrations;
using NGB.Runtime.Catalogs;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmCatalogSchemaValidation_NoRepair_P0Tests(PmIntegrationFixture fixture)
{
    [Fact]
    public async Task CatalogSchemaValidation_AfterCleanMigrate_WithoutRepair_Passes()
    {
        await using var db = await TemporaryDatabase.CreateAsync(fixture.ConnectionString, "ngb_pm_catalog_schema");

        var packs = SchemaMigrator.DiscoverPacks(
        [
            typeof(DatabaseBootstrapper).Assembly,
            typeof(PropertyManagementMigrationPackContributor).Assembly
        ]);

        await SchemaMigrator.MigrateAsync(
            db.ConnectionString,
            packs,
            includePackIds: ["pm"],
            repair: false,
            dryRun: false,
            log: null);

        using var factory = new PmApiFactory(fixture, db.ConnectionString);
        await using var scope = factory.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<ICatalogSchemaValidationService>();

        await validator.Invoking(v => v.ValidateAllAsync(CancellationToken.None))
            .Should().NotThrowAsync();
    }
}
