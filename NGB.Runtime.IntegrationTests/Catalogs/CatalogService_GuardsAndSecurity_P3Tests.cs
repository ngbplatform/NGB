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
public sealed class CatalogService_GuardsAndSecurity_P3Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string CatalogCode = "it_cat_universal";
    private const string HeadTable = "cat_it_cat_universal";
    private const string DisplayColumn = "name";

    [Fact]
    public async Task GetPageAsync_WhenOffsetIsNegative_ThrowsArgumentOutOfRange()
    {
        await EnsureHeadTableExistsAsync(Fixture.ConnectionString);

        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICatalogService>();

        await FluentActions.Awaiting(() => svc.GetPageAsync(
                CatalogCode,
                new PageRequestDto(Offset: -1, Limit: 10),
                CancellationToken.None))
            .Should()
            .ThrowAsync<NgbArgumentOutOfRangeException>()
            .Where(e => e.ParamName == "offset");
    }

    [Fact]
    public async Task GetPageAsync_WhenLimitIsNonPositive_ThrowsArgumentOutOfRange()
    {
        await EnsureHeadTableExistsAsync(Fixture.ConnectionString);

        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICatalogService>();

        await FluentActions.Awaiting(() => svc.GetPageAsync(
                CatalogCode,
                new PageRequestDto(Offset: 0, Limit: 0),
                CancellationToken.None))
            .Should()
            .ThrowAsync<NgbArgumentOutOfRangeException>()
            .Where(e => e.ParamName == "limit");
    }

    [Fact]
    public async Task GetPageAsync_OrdersNullDisplayLast_WhenHeadRowIsMissing()
    {
        await EnsureHeadTableExistsAsync(Fixture.ConnectionString);

        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();

        var drafts = scope.ServiceProvider.GetRequiredService<ICatalogDraftService>();
        var svc = scope.ServiceProvider.GetRequiredService<ICatalogService>();

        // Create a catalog row without a head row (Display will be NULL).
        var noHeadId = await drafts.CreateAsync(CatalogCode, ct: CancellationToken.None);

        // Create a normal row with Display.
        var alpha = await svc.CreateAsync(CatalogCode, new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement("Alpha")
        }), CancellationToken.None);

        var page = await svc.GetPageAsync(CatalogCode, new PageRequestDto(Offset: 0, Limit: 50), CancellationToken.None);

        page.Items.Select(x => x.Id).Should().Contain(new[] { noHeadId, alpha.Id });
        page.Items.Last().Id.Should().Be(noHeadId);
        page.Items.First().Id.Should().Be(alpha.Id);
        page.Items.Last().Display.Should().BeNull();
    }

    [Fact]
    public async Task GetPageAsync_IgnoresWhitespaceFilterKeys()
    {
        await EnsureHeadTableExistsAsync(Fixture.ConnectionString);

        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICatalogService>();

        await svc.CreateAsync(CatalogCode, new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement("A"),
            ["age"] = JsonSerializer.SerializeToElement(20)
        }), CancellationToken.None);

        await svc.CreateAsync(CatalogCode, new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement("B"),
            ["age"] = JsonSerializer.SerializeToElement(10)
        }), CancellationToken.None);

        var page = await svc.GetPageAsync(
            CatalogCode,
            new PageRequestDto(
                Offset: 0,
                Limit: 50,
                Filters: new Dictionary<string, string>
                {
                    ["   "] = "noise",
                    ["age"] = "20"
                }),
            CancellationToken.None);

        page.Total.Should().Be(1);
        page.Items.Single().Display.Should().Be("A");
    }

    [Fact]
    public async Task GetPageAsync_FilterOrderDoesNotAffectResults()
    {
        await EnsureHeadTableExistsAsync(Fixture.ConnectionString);

        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICatalogService>();

        var a = await svc.CreateAsync(CatalogCode, new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement("Alpha"),
            ["age"] = JsonSerializer.SerializeToElement(20),
            ["email"] = JsonSerializer.SerializeToElement("a@example.com")
        }), CancellationToken.None);

        await svc.CreateAsync(CatalogCode, new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement("Beta"),
            ["age"] = JsonSerializer.SerializeToElement(20),
            ["email"] = JsonSerializer.SerializeToElement("b@example.com")
        }), CancellationToken.None);

        var page1 = await svc.GetPageAsync(
            CatalogCode,
            new PageRequestDto(Offset: 0, Limit: 50, Filters: new Dictionary<string, string>
            {
                ["age"] = "20",
                ["email"] = "a@example.com"
            }),
            CancellationToken.None);

        var page2 = await svc.GetPageAsync(
            CatalogCode,
            new PageRequestDto(Offset: 0, Limit: 50, Filters: new Dictionary<string, string>
            {
                ["email"] = "a@example.com",
                ["age"] = "20"
            }),
            CancellationToken.None);

        page1.Items.Select(x => x.Id).Should().Equal(a.Id);
        page2.Items.Select(x => x.Id).Should().Equal(a.Id);
    }

    [Fact]
    public async Task GetPageAsync_IsResistantToSqlInjectionInSearch_AndDoesNotBreakSubsequentWrites()
    {
        await EnsureHeadTableExistsAsync(Fixture.ConnectionString);

        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICatalogService>();

        await svc.CreateAsync(CatalogCode, new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement("Alpha")
        }), CancellationToken.None);

        // Should not throw or modify schema.
        var injection = "%' ; DROP TABLE catalogs; --";
        var page = await svc.GetPageAsync(CatalogCode, new PageRequestDto(Offset: 0, Limit: 50, Search: injection), CancellationToken.None);
        page.Items.Should().BeEmpty();

        // Verify writes still work (catalogs table is intact).
        var created = await svc.CreateAsync(CatalogCode, new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement("StillWorks")
        }), CancellationToken.None);

        created.Display.Should().Be("StillWorks");
    }

    [Fact]
    public async Task GetPageAsync_IsResistantToSqlInjectionInFilters()
    {
        await EnsureHeadTableExistsAsync(Fixture.ConnectionString);

        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICatalogService>();

        await svc.CreateAsync(CatalogCode, new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement("A"),
            ["age"] = JsonSerializer.SerializeToElement(20)
        }), CancellationToken.None);

        await svc.CreateAsync(CatalogCode, new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement("B"),
            ["age"] = JsonSerializer.SerializeToElement(10)
        }), CancellationToken.None);

        var page = await svc.GetPageAsync(
            CatalogCode,
            new PageRequestDto(Offset: 0, Limit: 50, Filters: new Dictionary<string, string>
            {
                ["age"] = "20 OR 1=1"
            }),
            CancellationToken.None);

        page.Total.Should().Be(0);
        page.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateAsync_WhenPayloadHasNoFields_IsNoOp_AndDoesNotNullOutColumns()
    {
        await EnsureHeadTableExistsAsync(Fixture.ConnectionString);

        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICatalogService>();

        var created = await svc.CreateAsync(CatalogCode, new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement("Alpha"),
            ["email"] = JsonSerializer.SerializeToElement("alpha@example.com")
        }), CancellationToken.None);

        var updated = await svc.UpdateAsync(CatalogCode, created.Id, new RecordPayload(Fields: null, Parts: null), CancellationToken.None);

        updated.Display.Should().Be("Alpha");
        updated.Payload.Fields!["email"].GetString().Should().Be("alpha@example.com");
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
