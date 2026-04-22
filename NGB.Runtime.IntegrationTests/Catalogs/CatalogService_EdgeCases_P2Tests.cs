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
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Catalogs;

[Collection(PostgresCollection.Name)]
public sealed class CatalogService_EdgeCases_P2Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string CatalogCode = "it_cat_universal";
    private const string HeadTable = "cat_it_cat_universal";
    private const string DisplayColumn = "name";

    [Fact]
    public async Task LookupAsync_FallsBackToId_WhenHeadRowIsMissing()
    {
        await EnsureHeadTableExistsAsync(Fixture.ConnectionString);

        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();

        var drafts = scope.ServiceProvider.GetRequiredService<ICatalogDraftService>();
        var svc = scope.ServiceProvider.GetRequiredService<ICatalogService>();

        // Create a catalog row without a head row.
        var id = await drafts.CreateAsync(CatalogCode, ct: CancellationToken.None);

        var lookup = await svc.LookupAsync(CatalogCode, query: null, limit: 100, CancellationToken.None);

        var item = lookup.Single(x => x.Id == id);
        item.Label.Should().Be(id.ToString());
    }

    [Fact]
    public async Task GetByIdAsync_WhenHeadRowIsMissing_ReturnsNullDisplay_AndNullFields()
    {
        await EnsureHeadTableExistsAsync(Fixture.ConnectionString);

        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();

        var drafts = scope.ServiceProvider.GetRequiredService<ICatalogDraftService>();
        var svc = scope.ServiceProvider.GetRequiredService<ICatalogService>();

        var id = await drafts.CreateAsync(CatalogCode, ct: CancellationToken.None);

        var row = await svc.GetByIdAsync(CatalogCode, id, CancellationToken.None);
        row.Display.Should().BeNull();
        row.Payload.Fields!.Should().ContainKey("name");
        row.Payload.Fields!["name"].ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task GetByIdsAsync_PreservesOrderAndDuplicates_AndOmitsUnknownIds()
    {
        await EnsureHeadTableExistsAsync(Fixture.ConnectionString);

        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICatalogService>();

        var a = await svc.CreateAsync(CatalogCode, new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement("Alpha")
        }), CancellationToken.None);

        var b = await svc.CreateAsync(CatalogCode, new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement("Beta")
        }), CancellationToken.None);

        var missing = Guid.NewGuid();
        var ids = new[] { b.Id, a.Id, b.Id, missing };

        var rows = await svc.GetByIdsAsync(CatalogCode, ids, CancellationToken.None);

        rows.Select(x => x.Id).Should().Equal(b.Id, a.Id, b.Id);
    }

    [Fact]
    public async Task GetPageAsync_IncludesMarkedForDeletion_AndExposesTheFlag()
    {
        await EnsureHeadTableExistsAsync(Fixture.ConnectionString);

        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICatalogService>();

        var a = await svc.CreateAsync(CatalogCode, new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement("Alpha")
        }), CancellationToken.None);

        var b = await svc.CreateAsync(CatalogCode, new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement("Beta")
        }), CancellationToken.None);

        await svc.MarkForDeletionAsync(CatalogCode, b.Id, CancellationToken.None);

        var page = await svc.GetPageAsync(CatalogCode, new PageRequestDto(Offset: 0, Limit: 50), CancellationToken.None);

        page.Items.Select(x => x.Id).Should().Contain(new[] { a.Id, b.Id });
        page.Items.Single(x => x.Id == a.Id).IsMarkedForDeletion.Should().BeFalse();
        page.Items.Single(x => x.Id == b.Id).IsMarkedForDeletion.Should().BeTrue();
    }

    [Fact]
    public async Task GetPageAsync_FiltersByMultipleColumns_UsingTextComparison()
    {
        await EnsureHeadTableExistsAsync(Fixture.ConnectionString);

        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICatalogService>();

        await svc.CreateAsync(CatalogCode, new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement("A"),
            ["age"] = JsonSerializer.SerializeToElement(20),
            ["is_active"] = JsonSerializer.SerializeToElement(true)
        }), CancellationToken.None);

        await svc.CreateAsync(CatalogCode, new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement("B"),
            ["age"] = JsonSerializer.SerializeToElement(20),
            ["is_active"] = JsonSerializer.SerializeToElement(false)
        }), CancellationToken.None);

        await svc.CreateAsync(CatalogCode, new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement("C"),
            ["age"] = JsonSerializer.SerializeToElement(10),
            ["is_active"] = JsonSerializer.SerializeToElement(true)
        }), CancellationToken.None);

        var page = await svc.GetPageAsync(
            CatalogCode,
            new PageRequestDto(
                Offset: 0,
                Limit: 50,
                Filters: new Dictionary<string, string>
                {
                    ["age"] = "20",
                    ["is_active"] = "true",
                }),
            CancellationToken.None);

        page.Total.Should().Be(1);
        page.Items.Single().Display.Should().Be("A");
    }

    [Fact]
    public async Task CreateAsync_DecimalParsing_UsesInvariantCulture()
    {
        await EnsureHeadTableExistsAsync(Fixture.ConnectionString);

        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICatalogService>();

        var ok = await svc.CreateAsync(CatalogCode, new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement("Rent"),
            ["rent_amount"] = JsonSerializer.SerializeToElement("12.34")
        }), CancellationToken.None);

        ok.Payload.Fields!["rent_amount"].GetDecimal().Should().Be(12.34m);

        await FluentActions.Awaiting(() => svc.CreateAsync(CatalogCode, new RecordPayload(new Dictionary<string, JsonElement>
            {
                ["name"] = JsonSerializer.SerializeToElement("Bad"),
                ["rent_amount"] = JsonSerializer.SerializeToElement("12,34")
            }),
            CancellationToken.None))
            .Should()
            .ThrowAsync<NgbArgumentInvalidException>()
            .WithMessage("*Enter a valid number for Rent Amount.*");
    }

    private static async Task EnsureHeadTableExistsAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        var sql = $"""
                  CREATE TABLE IF NOT EXISTS {HeadTable} (
                      catalog_id             uuid PRIMARY KEY,
                      name                   text NOT NULL,
                      email                  text NULL,
                      age                    int NULL,
                      rent_amount            numeric(18,2) NULL,
                      is_active              boolean NULL,
                      ref_id                 uuid NULL,
                      move_in_date           date NULL,
                      last_contacted_at_utc  timestamptz NULL,
                      extra_json             jsonb NULL,

                      CONSTRAINT fk_{HeadTable}__catalog
                          FOREIGN KEY (catalog_id) REFERENCES catalogs(id)
                          ON DELETE CASCADE
                  );
                  """;

        await conn.ExecuteAsync(sql);
    }

    private IHost CreateHost()
        => IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services => services.AddSingleton<IDefinitionsContributor, UniversalCatalogContributor>());

    private sealed class UniversalCatalogContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddCatalog(CatalogCode, b => b.Metadata(new CatalogTypeMetadata(
                CatalogCode: CatalogCode,
                DisplayName: "IT Catalog Universal",
                Tables:
                [
                    new CatalogTableMetadata(
                        TableName: HeadTable,
                        Kind: TableKind.Head,
                        Columns:
                        [
                            new("catalog_id", ColumnType.Guid, Required: true),
                            new("name", ColumnType.String, Required: true, MaxLength: 200),
                            new("email", ColumnType.String, MaxLength: 200),
                            new("age", ColumnType.Int32),
                            new("rent_amount", ColumnType.Decimal),
                            new("is_active", ColumnType.Boolean),
                            new("ref_id", ColumnType.Guid),
                            new("move_in_date", ColumnType.Date),
                            new("last_contacted_at_utc", ColumnType.DateTimeUtc),
                            new("extra_json", ColumnType.Json),
                        ],
                        Indexes: [])
                ],
                Presentation: new CatalogPresentationMetadata(HeadTable, DisplayColumn),
                Version: new CatalogMetadataVersion(1, "integration-tests"))));
        }
    }
}
