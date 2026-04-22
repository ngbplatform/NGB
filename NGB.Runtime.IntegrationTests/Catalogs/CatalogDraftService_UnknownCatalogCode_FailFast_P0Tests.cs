using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Definitions;
using NGB.Core.Catalogs;
using NGB.Core.Catalogs.Exceptions;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Runtime.Catalogs;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Catalogs;

[Collection(PostgresCollection.Name)]
public sealed class CatalogDraftService_UnknownCatalogCode_FailFast_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string KnownCatalogCode = "it_cat_known";
    private const string UnknownCatalogCode = "it_cat_unknown";

    [Fact]
    public async Task CreateAsync_WhenCatalogCodeIsUnknown_ThrowsAndDoesNotWriteAnything()
    {
        // Arrange
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services => services.AddSingleton<IDefinitionsContributor, KnownCatalogContributor>());

        await using var scope = host.Services.CreateAsyncScope();

        var drafts = scope.ServiceProvider.GetRequiredService<ICatalogDraftService>();

        // Act
        var act = () => drafts.CreateAsync(UnknownCatalogCode);

        // Assert
        await act.Should().ThrowAsync<CatalogTypeNotFoundException>()
            .WithMessage($"Unknown catalog code '{UnknownCatalogCode}'.");

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var catalogCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM catalogs WHERE catalog_code = @catalogCode;",
            new { catalogCode = UnknownCatalogCode });
        catalogCount.Should().Be(0);

        var auditCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM platform_audit_events;");
        auditCount.Should().Be(0);
    }

    sealed class KnownCatalogContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddCatalog(KnownCatalogCode, b => b.Metadata(new CatalogTypeMetadata(
                CatalogCode: KnownCatalogCode,
                DisplayName: "Known IT Catalog",
                Tables: Array.Empty<CatalogTableMetadata>(),
                Presentation: new CatalogPresentationMetadata(TableName: "catalogs", DisplayColumn: "catalog_code"),
                Version: new CatalogMetadataVersion(1, "it"))));
        }
    }
}
