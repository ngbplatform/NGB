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
public sealed class CatalogService_TypeParsingAndInputGuards_P6Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string CatalogCode = "it_cat_universal";
    private const string HeadTable = "cat_it_cat_universal";
    private const string DisplayColumn = "name";

    [Fact]
    public async Task CreateAsync_ParsesScalarTypes_FromStringsToo()
    {
        await EnsureHeadTableExistsAsync(Fixture.ConnectionString);

        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();

        var svc = scope.ServiceProvider.GetRequiredService<ICatalogService>();

        var refId = Guid.CreateVersion7();

        var created = await svc.CreateAsync(CatalogCode, new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement("John"),
            ["age"] = JsonSerializer.SerializeToElement("30"),
            ["rent_amount"] = JsonSerializer.SerializeToElement("12.34"),
            ["is_active"] = JsonSerializer.SerializeToElement("true"),
            ["ref_id"] = JsonSerializer.SerializeToElement(refId.ToString()),
            ["move_in_date"] = JsonSerializer.SerializeToElement("2026-02-21"),
            ["last_contacted_at_utc"] = JsonSerializer.SerializeToElement("2026-02-21T00:00:00Z"),
        }), CancellationToken.None);

        var read = await svc.GetByIdAsync(CatalogCode, created.Id, CancellationToken.None);

        read.Payload.Fields!["age"].GetInt32().Should().Be(30);
        read.Payload.Fields!["rent_amount"].GetDecimal().Should().Be(12.34m);
        read.Payload.Fields!["is_active"].GetBoolean().Should().BeTrue();
        read.Payload.Fields!["ref_id"].GetString().Should().Be(refId.ToString());
        read.Payload.Fields!["move_in_date"].GetString().Should().Be("2026-02-21");
        read.Payload.Fields!["last_contacted_at_utc"].GetString().Should().StartWith("2026-02-21T00:00:00");
    }

    [Fact]
    public async Task CreateAsync_DateTimeUtc_RejectsUnspecifiedOrOffset()
    {
        await EnsureHeadTableExistsAsync(Fixture.ConnectionString);

        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICatalogService>();

        // Unspecified kind
        await FluentActions.Awaiting(() => svc.CreateAsync(CatalogCode, new RecordPayload(new Dictionary<string, JsonElement>
            {
                ["name"] = JsonSerializer.SerializeToElement("A"),
                ["last_contacted_at_utc"] = JsonSerializer.SerializeToElement("2026-02-21T00:00:00")
            }),
            CancellationToken.None))
            .Should()
            .ThrowAsync<NgbArgumentInvalidException>()
            .WithMessage("*Enter a valid date and time for Last Contacted At.*");

        // Has offset (not UTC)
        await FluentActions.Awaiting(() => svc.CreateAsync(CatalogCode, new RecordPayload(new Dictionary<string, JsonElement>
            {
                ["name"] = JsonSerializer.SerializeToElement("B"),
                ["last_contacted_at_utc"] = JsonSerializer.SerializeToElement("2026-02-21T00:00:00+02:00")
            }),
            CancellationToken.None))
            .Should()
            .ThrowAsync<NgbArgumentInvalidException>()
            .WithMessage("*Enter a valid date and time for Last Contacted At.*");
    }

    [Fact]
    public async Task UpdateAsync_RejectsNullForRequiredFields()
    {
        await EnsureHeadTableExistsAsync(Fixture.ConnectionString);

        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICatalogService>();

        var created = await svc.CreateAsync(CatalogCode, new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement("John"),
            ["email"] = JsonSerializer.SerializeToElement("a@a.com")
        }), CancellationToken.None);

        await FluentActions.Awaiting(() => svc.UpdateAsync(CatalogCode, created.Id,
                new RecordPayload(new Dictionary<string, JsonElement>
                {
                    ["name"] = JsonSerializer.SerializeToElement((string?)null)
                }),
                CancellationToken.None))
            .Should()
            .ThrowAsync<NgbArgumentInvalidException>()
            .WithMessage("*Name is required.*");
    }

    [Fact]
    public async Task GetByIdAsync_WhenIdIsEmpty_ThrowsArgumentRequired()
    {
        await EnsureHeadTableExistsAsync(Fixture.ConnectionString);

        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICatalogService>();

        await FluentActions.Awaiting(() => svc.GetByIdAsync(CatalogCode, Guid.Empty, CancellationToken.None))
            .Should()
            .ThrowAsync<NgbArgumentRequiredException>()
            .Where(e => e.ParamName == "id");
    }

    [Fact]
    public async Task CreateAsync_WhenFieldsIsNull_ThrowsMissingRequiredField()
    {
        await EnsureHeadTableExistsAsync(Fixture.ConnectionString);

        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICatalogService>();

        await FluentActions.Awaiting(() => svc.CreateAsync(CatalogCode, new RecordPayload(Fields: null), CancellationToken.None))
            .Should()
            .ThrowAsync<NgbArgumentInvalidException>()
            .WithMessage("*Name is required.*");
    }

    [Fact]
    public async Task GetPageAsync_FiltersWork_ForGuidDateAndBoolean()
    {
        await EnsureHeadTableExistsAsync(Fixture.ConnectionString);

        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICatalogService>();

        var g1 = Guid.CreateVersion7();
        var g2 = Guid.CreateVersion7();

        var a = await svc.CreateAsync(CatalogCode, new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement("Alpha"),
            ["is_active"] = JsonSerializer.SerializeToElement(true),
            ["move_in_date"] = JsonSerializer.SerializeToElement("2026-02-21"),
            ["ref_id"] = JsonSerializer.SerializeToElement(g1.ToString())
        }), CancellationToken.None);

        await svc.CreateAsync(CatalogCode, new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement("Beta"),
            ["is_active"] = JsonSerializer.SerializeToElement(true),
            ["move_in_date"] = JsonSerializer.SerializeToElement("2026-02-22"),
            ["ref_id"] = JsonSerializer.SerializeToElement(g1.ToString())
        }), CancellationToken.None);

        await svc.CreateAsync(CatalogCode, new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement("Gamma"),
            ["is_active"] = JsonSerializer.SerializeToElement(false),
            ["move_in_date"] = JsonSerializer.SerializeToElement("2026-02-21"),
            ["ref_id"] = JsonSerializer.SerializeToElement(g2.ToString())
        }), CancellationToken.None);

        var page = await svc.GetPageAsync(CatalogCode, new PageRequestDto(
            Offset: 0,
            Limit: 50,
            Filters: new Dictionary<string, string>
            {
                ["is_active"] = "true",
                ["move_in_date"] = "2026-02-21",
                ["ref_id"] = g1.ToString(),
            }), CancellationToken.None);

        page.Total.Should().Be(1);
        page.Items.Single().Id.Should().Be(a.Id);
    }

    [Fact]
    public async Task GetPageAsync_Search_IsUnicodeFriendly_AndCaseInsensitive()
    {
        await EnsureHeadTableExistsAsync(Fixture.ConnectionString);

        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICatalogService>();

        await svc.CreateAsync(CatalogCode, new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement("Zoë 🏠")
        }), CancellationToken.None);

        var page = await svc.GetPageAsync(CatalogCode, new PageRequestDto(Offset: 0, Limit: 50, Search: "zoë"), CancellationToken.None);

        page.Total.Should().Be(1);
        page.Items.Single().Display.Should().Be("Zoë 🏠");
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
