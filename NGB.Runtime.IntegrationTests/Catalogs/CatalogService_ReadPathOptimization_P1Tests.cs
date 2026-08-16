using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Definitions;
using NGB.Metadata.Base;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Runtime.Catalogs;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Catalogs;

[Collection(SchemaPostgresCollection.Name)]
public sealed class CatalogService_ReadPathOptimization_P1Tests(SchemaPostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string CatalogCode = "it_cat_read_path";
    private const string HeadTable = "cat_it_cat_read_path";
    private const string DisplayColumn = "name";

    [Fact]
    public async Task GetPageAsync_WithoutHeadCriteria_PagesAcrossHeadRowsAndMissingHeadRows()
    {
        await EnsureCleanCatalogAsync(Fixture.ConnectionString);

        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();

        var drafts = scope.ServiceProvider.GetRequiredService<ICatalogDraftService>();
        var svc = scope.ServiceProvider.GetRequiredService<ICatalogService>();

        await svc.CreateAsync(CatalogCode, new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement("Alpha")
        }), CancellationToken.None);

        await svc.CreateAsync(CatalogCode, new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement("Beta")
        }), CancellationToken.None);

        var orphanId = await drafts.CreateAsync(CatalogCode, ct: CancellationToken.None);

        var page = await svc.GetPageAsync(
            CatalogCode,
            new PageRequestDto(Offset: 2, Limit: 10),
            CancellationToken.None);

        page.Total.Should().Be(3);
        page.Items.Should().ContainSingle();
        page.Items[0].Id.Should().Be(orphanId);
        page.Items[0].Display.Should().BeNull();
    }

    [Fact]
    public async Task LookupAsync_WithSearch_FallsBackToId_WhenHeadRowIsMissing()
    {
        await EnsureCleanCatalogAsync(Fixture.ConnectionString);

        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();

        var drafts = scope.ServiceProvider.GetRequiredService<ICatalogDraftService>();
        var svc = scope.ServiceProvider.GetRequiredService<ICatalogService>();

        var orphanId = await drafts.CreateAsync(CatalogCode, ct: CancellationToken.None);

        var lookup = await svc.LookupAsync(
            CatalogCode,
            query: orphanId.ToString("D")[..8],
            limit: 10,
            CancellationToken.None);

        lookup.Should().ContainSingle(x => x.Id == orphanId && x.Label == orphanId.ToString("D"));
    }

    private static async Task EnsureCleanCatalogAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        var sql = $"""
                  CREATE TABLE IF NOT EXISTS {HeadTable} (
                      catalog_id uuid PRIMARY KEY,
                      name       text NOT NULL,

                      CONSTRAINT fk_{HeadTable}__catalog
                          FOREIGN KEY (catalog_id) REFERENCES catalogs(id)
                          ON DELETE CASCADE
                  );

                  DELETE FROM catalogs
                   WHERE catalog_code = @catalogCode;
                  """;

        await conn.ExecuteAsync(sql, new { catalogCode = CatalogCode });
    }

    private IHost CreateHost()
        => IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services => services.AddSingleton<IDefinitionsContributor, ReadPathCatalogContributor>());

    private sealed class ReadPathCatalogContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddCatalog(CatalogCode, b => b.Metadata(new CatalogTypeMetadata(
                CatalogCode: CatalogCode,
                DisplayName: "IT Catalog Read Path",
                Tables:
                [
                    new CatalogTableMetadata(
                        TableName: HeadTable,
                        Kind: TableKind.Head,
                        Columns:
                        [
                            new("catalog_id", ColumnType.Guid, Required: true),
                            new("name", ColumnType.String, Required: true, MaxLength: 200),
                        ],
                        Indexes: [])
                ],
                Presentation: new CatalogPresentationMetadata(HeadTable, DisplayColumn),
                Version: new CatalogMetadataVersion(1, "integration-tests"))));
        }
    }
}
