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
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Catalogs;

[Collection(PostgresCollection.Name)]
public sealed class CatalogService_PagingAndSearch_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string CatalogCode = "it_cat_universal";
    private const string HeadTable = "cat_it_cat_universal";
    private const string DisplayColumn = "name";

    [Fact]
    public async Task GetAllMetadataAsync_IsSortedByCatalogType_AndContainsTestCatalog()
    {
        await EnsureHeadTableExistsAsync(Fixture.ConnectionString);

        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICatalogService>();

        var all = await svc.GetAllMetadataAsync(CancellationToken.None);

        all.Select(x => x.CatalogType)
            .Should()
            .BeInAscendingOrder(StringComparer.Ordinal);

        all.Should().Contain(x => x.CatalogType == CatalogCode);
    }

    [Fact]
    public async Task GetTypeMetadataAsync_ListHasAtMostSixScalarColumns_AndSkipsJson()
    {
        await EnsureHeadTableExistsAsync(Fixture.ConnectionString);

        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICatalogService>();

        var meta = await svc.GetTypeMetadataAsync(CatalogCode, CancellationToken.None);

        meta.List!.Columns.Should().HaveCount(6);
        meta.List!.Columns.Select(c => c.Key).Should().NotContain("extra_json");
    }

    [Fact]
    public async Task GetPageAsync_Paginates_AndOrdersByDisplayThenId()
    {
        await EnsureHeadTableExistsAsync(Fixture.ConnectionString);

        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICatalogService>();

        for (var i = 0; i < 25; i++)
        {
            await svc.CreateAsync(CatalogCode, new RecordPayload(new Dictionary<string, JsonElement>
            {
                ["name"] = JsonSerializer.SerializeToElement($"N{i:000}")
            }), CancellationToken.None);
        }

        var p1 = await svc.GetPageAsync(CatalogCode, new PageRequestDto(Offset: 0, Limit: 10), CancellationToken.None);
        p1.Total.Should().Be(25);
        p1.Items.Should().HaveCount(10);
        p1.Items.Select(x => x.Display).Should().Equal(Enumerable.Range(0, 10).Select(i => $"N{i:000}"));

        var p2 = await svc.GetPageAsync(CatalogCode, new PageRequestDto(Offset: 10, Limit: 10), CancellationToken.None);
        p2.Total.Should().Be(25);
        p2.Items.Should().HaveCount(10);
        p2.Items.Select(x => x.Display).Should().Equal(Enumerable.Range(10, 10).Select(i => $"N{i:000}"));

        var p3 = await svc.GetPageAsync(CatalogCode, new PageRequestDto(Offset: 20, Limit: 10), CancellationToken.None);
        p3.Total.Should().Be(25);
        p3.Items.Should().HaveCount(5);
        p3.Items.Select(x => x.Display).Should().Equal(Enumerable.Range(20, 5).Select(i => $"N{i:000}"));
    }

    [Fact]
    public async Task GetPageAsync_RejectsInvalidOffsetOrLimit()
    {
        await EnsureHeadTableExistsAsync(Fixture.ConnectionString);

        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICatalogService>();

        await FluentActions.Awaiting(() => svc.GetPageAsync(CatalogCode, new PageRequestDto(Offset: -1, Limit: 10), CancellationToken.None))
            .Should()
            .ThrowAsync<NgbArgumentOutOfRangeException>();

        await FluentActions.Awaiting(() => svc.GetPageAsync(CatalogCode, new PageRequestDto(Offset: 0, Limit: 0), CancellationToken.None))
            .Should()
            .ThrowAsync<NgbArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task GetPageAsync_Search_IsCaseInsensitive_AndWhitespaceBecomesNoSearch()
    {
        await EnsureHeadTableExistsAsync(Fixture.ConnectionString);

        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICatalogService>();

        await svc.CreateAsync(CatalogCode, new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement("Alice")
        }), CancellationToken.None);

        await svc.CreateAsync(CatalogCode, new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement("Bob")
        }), CancellationToken.None);

        var s1 = await svc.GetPageAsync(CatalogCode, new PageRequestDto(Offset: 0, Limit: 50, Search: "aLi"), CancellationToken.None);
        s1.Total.Should().Be(1);
        s1.Items.Single().Display.Should().Be("Alice");

        var s2 = await svc.GetPageAsync(CatalogCode, new PageRequestDto(Offset: 0, Limit: 50, Search: "   "), CancellationToken.None);
        s2.Total.Should().Be(2);
        s2.Items.Select(x => x.Display).Should().Equal("Alice", "Bob");
    }

    [Fact]
    public async Task GetPageAsync_SearchAndFilters_Compose()
    {
        await EnsureHeadTableExistsAsync(Fixture.ConnectionString);

        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICatalogService>();

        await svc.CreateAsync(CatalogCode, new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement("Alpha"),
            ["age"] = JsonSerializer.SerializeToElement(10)
        }), CancellationToken.None);

        await svc.CreateAsync(CatalogCode, new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement("Alpine"),
            ["age"] = JsonSerializer.SerializeToElement(20)
        }), CancellationToken.None);

        await svc.CreateAsync(CatalogCode, new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement("Beta"),
            ["age"] = JsonSerializer.SerializeToElement(20)
        }), CancellationToken.None);

        var page = await svc.GetPageAsync(
            CatalogCode,
            new PageRequestDto(
                Offset: 0,
                Limit: 50,
                Search: "al",
                Filters: new Dictionary<string, string> { ["age"] = "20" }),
            CancellationToken.None);

        page.Total.Should().Be(1);
        page.Items.Single().Display.Should().Be("Alpine");
    }

    [Fact]
    public async Task LookupAsync_WhenQueryIsNull_ReturnsOrderedNonDeletedItems()
    {
        await EnsureHeadTableExistsAsync(Fixture.ConnectionString);

        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICatalogService>();

        var c = await svc.CreateAsync(CatalogCode, new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement("Charlie")
        }), CancellationToken.None);

        var a = await svc.CreateAsync(CatalogCode, new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement("Alpha")
        }), CancellationToken.None);

        var b = await svc.CreateAsync(CatalogCode, new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement("Beta")
        }), CancellationToken.None);

        await svc.MarkForDeletionAsync(CatalogCode, b.Id, CancellationToken.None);

        var lookup = await svc.LookupAsync(CatalogCode, query: null, limit: 10, CancellationToken.None);
        // Ordered by display, excluding deleted (Beta).
        lookup.Select(x => x.Id).Should().Equal(a.Id, c.Id);
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
