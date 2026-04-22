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
public sealed class CatalogService_UniversalCrud_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    // Use unique catalogCode/table names to avoid colliding with real module typed tables.
    private const string CatalogCode = "it_cat_universal";
    private const string HeadTable = "cat_it_cat_universal";
    private const string PartTable = "cat_it_cat_universal__contact_rows";
    private const string DisplayColumn = "name";
    private const string PartCode = "contacts";

    [Fact]
    public async Task GetTypeMetadataAsync_ExposesHeadAndPartFields_AndSkipsJsonFields()
    {
        await EnsureTablesExistAsync(Fixture.ConnectionString);

        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();

        var svc = scope.ServiceProvider.GetRequiredService<ICatalogService>();

        var meta = await svc.GetTypeMetadataAsync(CatalogCode, CancellationToken.None);

        meta.CatalogType.Should().Be(CatalogCode);
        meta.Form.Should().NotBeNull();
        meta.List.Should().NotBeNull();

        // json column is excluded from UI metadata
        var formFieldKeys = meta.Form!.Sections
            .SelectMany(s => s.Rows)
            .SelectMany(r => r.Fields)
            .Select(f => f.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        formFieldKeys.Should().Contain("name");
        formFieldKeys.Should().Contain("email");
        formFieldKeys.Should().Contain("age");
        formFieldKeys.Should().NotContain("extra_json");

        meta.Parts.Should().NotBeNull();
        meta.Parts!.Should().ContainSingle(p => p.PartCode == PartCode);
        meta.Parts![0].List.Columns.Select(c => c.Key).Should().Equal("ordinal", "contact_name", "contact_email");
    }

    [Fact]
    public async Task CreateAsync_ThenGetById_RoundtripsAllSupportedScalarTypes()
    {
        await EnsureTablesExistAsync(Fixture.ConnectionString);

        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();

        var svc = scope.ServiceProvider.GetRequiredService<ICatalogService>();

        var refId = Guid.CreateVersion7();
        var payload = new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement("John Doe"),
            ["email"] = JsonSerializer.SerializeToElement("john.doe@example.com"),
            ["age"] = JsonSerializer.SerializeToElement(30),
            ["rent_amount"] = JsonSerializer.SerializeToElement(1234.56m),
            ["is_active"] = JsonSerializer.SerializeToElement(true),
            ["ref_id"] = JsonSerializer.SerializeToElement(refId.ToString()),
            ["move_in_date"] = JsonSerializer.SerializeToElement("2026-02-21"),
            ["last_contacted_at_utc"] = JsonSerializer.SerializeToElement("2026-02-21T00:00:00Z"),
            // ColumnType.Json is stored as jsonb, but returned as a raw json string.
            ["extra_json"] = JsonSerializer.SerializeToElement(new { x = 1, y = "z" })
        });

        var created = await svc.CreateAsync(CatalogCode, payload, CancellationToken.None);
        created.Display.Should().Be("John Doe");
        created.IsMarkedForDeletion.Should().BeFalse();

        var read = await svc.GetByIdAsync(CatalogCode, created.Id, CancellationToken.None);

        read.Payload.Fields!.Should().ContainKey("name");
        read.Payload.Fields!["name"].GetString().Should().Be("John Doe");
        read.Payload.Fields!["email"].GetString().Should().Be("john.doe@example.com");
        read.Payload.Fields!["age"].GetInt32().Should().Be(30);
        read.Payload.Fields!["rent_amount"].GetDecimal().Should().Be(1234.56m);
        read.Payload.Fields!["is_active"].GetBoolean().Should().BeTrue();
        read.Payload.Fields!["ref_id"].GetString().Should().Be(refId.ToString());
        read.Payload.Fields!["move_in_date"].GetString().Should().Be("2026-02-21");

        // DateTimeUtc roundtrip: exact formatting is controlled by System.Text.Json, so assert on the instant.
        read.Payload.Fields!["last_contacted_at_utc"].GetString().Should().StartWith("2026-02-21T00:00:00");

        // jsonb roundtrip: returned as raw json text string (whitespace/formatting is not stable)
        var json = read.Payload.Fields!["extra_json"].GetString();
        json.Should().NotBeNullOrWhiteSpace();
        using var doc = JsonDocument.Parse(json!);
        doc.RootElement.GetProperty("x").GetInt32().Should().Be(1);
        doc.RootElement.GetProperty("y").GetString().Should().Be("z");
    }

    [Fact]
    public async Task GetPageAsync_WhenSearchIsNull_DoesNotThrow_AndReturnsItems()
    {
        await EnsureTablesExistAsync(Fixture.ConnectionString);

        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICatalogService>();

        await svc.CreateAsync(CatalogCode, new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement("Alice")
        }), CancellationToken.None);

        var page = await svc.GetPageAsync(CatalogCode, new PageRequestDto(Offset: 0, Limit: 50, Search: null), CancellationToken.None);
        page.Total.Should().Be(1);
        page.Items.Should().HaveCount(1);
        page.Items.Single().Display.Should().Be("Alice");
    }

    [Fact]
    public async Task GetPageAsync_WithFilters_FiltersByColumnValue_AndRejectsUnknownColumns()
    {
        await EnsureTablesExistAsync(Fixture.ConnectionString);

        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICatalogService>();

        await svc.CreateAsync(CatalogCode, new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement("A"),
            ["age"] = JsonSerializer.SerializeToElement(10)
        }), CancellationToken.None);

        await svc.CreateAsync(CatalogCode, new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement("B"),
            ["age"] = JsonSerializer.SerializeToElement(20)
        }), CancellationToken.None);

        var filtered = await svc.GetPageAsync(
            CatalogCode,
            new PageRequestDto(
                Offset: 0,
                Limit: 50,
                Filters: new Dictionary<string, string> { ["age"] = "20" }),
            CancellationToken.None);

        filtered.Total.Should().Be(1);
        filtered.Items.Single().Display.Should().Be("B");

        await FluentActions.Awaiting(() => svc.GetPageAsync(
                CatalogCode,
                new PageRequestDto(Offset: 0, Limit: 50, Filters: new Dictionary<string, string> { ["nope"] = "x" }),
                CancellationToken.None))
            .Should()
            .ThrowAsync<NgbArgumentInvalidException>()
            .WithMessage("*Filter*Nope*not available for this list*");
    }

    [Fact]
    public async Task UpdateAsync_IsPartial_AndRefusesUpdatesForDeletedCatalogs()
    {
        await EnsureTablesExistAsync(Fixture.ConnectionString);

        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICatalogService>();

        var created = await svc.CreateAsync(CatalogCode, new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement("John"),
            ["email"] = JsonSerializer.SerializeToElement("a@a.com")
        }), CancellationToken.None);

        // partial update (email only)
        var updated = await svc.UpdateAsync(CatalogCode, created.Id, new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["email"] = JsonSerializer.SerializeToElement("b@b.com")
        }), CancellationToken.None);

        updated.Payload.Fields!["name"].GetString().Should().Be("John");
        updated.Payload.Fields!["email"].GetString().Should().Be("b@b.com");

        await svc.MarkForDeletionAsync(CatalogCode, created.Id, CancellationToken.None);

        await FluentActions.Awaiting(() => svc.UpdateAsync(CatalogCode, created.Id, new RecordPayload(new Dictionary<string, JsonElement>
            {
                ["email"] = JsonSerializer.SerializeToElement("c@c.com")
            }),
            CancellationToken.None))
            .Should()
            .ThrowAsync<NgbArgumentInvalidException>()
            .WithMessage("*marked for deletion*");
    }

    [Fact]
    public async Task LookupAsync_ExcludesDeleted_WhileGetByIdsAsync_PreservesOrder_AndIncludesDeleted()
    {
        await EnsureTablesExistAsync(Fixture.ConnectionString);

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

        var lookup = await svc.LookupAsync(CatalogCode, query: null, limit: 10, CancellationToken.None);
        lookup.Select(x => x.Id).Should().BeEquivalentTo([a.Id]);

        var byIds = await svc.GetByIdsAsync(CatalogCode, [b.Id, a.Id], CancellationToken.None);
        byIds.Select(x => x.Id).Should().Equal(b.Id, a.Id);
    }

    [Fact]
    public async Task CreateAsync_AndUpdateAsync_WithParts_PersistsAndReplacesSpecifiedPartRows()
    {
        await EnsureTablesExistAsync(Fixture.ConnectionString);

        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICatalogService>();

        var created = await svc.CreateAsync(CatalogCode, new RecordPayload(
                Fields: new Dictionary<string, JsonElement>
                {
                    ["name"] = JsonSerializer.SerializeToElement("Part Owner"),
                    ["email"] = JsonSerializer.SerializeToElement("owner@example.com")
                },
                Parts: new Dictionary<string, RecordPartPayload>
                {
                    [PartCode] = new RecordPartPayload(
                    [
                        new Dictionary<string, JsonElement>
                        {
                            ["ordinal"] = JsonSerializer.SerializeToElement(1),
                            ["contact_name"] = JsonSerializer.SerializeToElement("Alice"),
                            ["contact_email"] = JsonSerializer.SerializeToElement("alice@example.com"),
                        },
                        new Dictionary<string, JsonElement>
                        {
                            ["ordinal"] = JsonSerializer.SerializeToElement(2),
                            ["contact_name"] = JsonSerializer.SerializeToElement("Bob"),
                            ["contact_email"] = JsonSerializer.SerializeToElement("bob@example.com"),
                        }
                    ])
                }),
            CancellationToken.None);

        created.Payload.Parts.Should().NotBeNull();
        created.Payload.Parts!.Should().ContainKey(PartCode);
        created.Payload.Parts![PartCode].Rows.Should().HaveCount(2);

        await using var verifyScope = host.Services.CreateAsyncScope();
        var verifyConn = verifyScope.ServiceProvider.GetRequiredService<NGB.Persistence.UnitOfWork.IUnitOfWork>().Connection;
        await verifyScope.ServiceProvider.GetRequiredService<NGB.Persistence.UnitOfWork.IUnitOfWork>().EnsureConnectionOpenAsync();

        var dbRows = (await verifyConn.QueryAsync<(int Ordinal, string ContactName, string? ContactEmail)>(
            $"SELECT ordinal, contact_name, contact_email FROM {PartTable} WHERE catalog_id = @id ORDER BY ordinal;",
            new { id = created.Id })).ToList();

        dbRows.Should().HaveCount(2);
        dbRows[0].Ordinal.Should().Be(1);
        dbRows[0].ContactName.Should().Be("Alice");
        dbRows[1].Ordinal.Should().Be(2);
        dbRows[1].ContactName.Should().Be("Bob");

        var updated = await svc.UpdateAsync(CatalogCode, created.Id, new RecordPayload(
                Fields: new Dictionary<string, JsonElement>
                {
                    ["email"] = JsonSerializer.SerializeToElement("updated@example.com")
                },
                Parts: new Dictionary<string, RecordPartPayload>
                {
                    [PartCode] = new RecordPartPayload(
                    [
                        new Dictionary<string, JsonElement>
                        {
                            ["ordinal"] = JsonSerializer.SerializeToElement(1),
                            ["contact_name"] = JsonSerializer.SerializeToElement("Carol"),
                            ["contact_email"] = JsonSerializer.SerializeToElement("carol@example.com"),
                        }
                    ])
                }),
            CancellationToken.None);

        updated.Payload.Fields!["name"].GetString().Should().Be("Part Owner");
        updated.Payload.Fields!["email"].GetString().Should().Be("updated@example.com");
        updated.Payload.Parts.Should().NotBeNull();
        updated.Payload.Parts![PartCode].Rows.Should().HaveCount(1);
        updated.Payload.Parts![PartCode].Rows[0]["contact_name"].GetString().Should().Be("Carol");

        var dbRows2 = (await verifyConn.QueryAsync<(int Ordinal, string ContactName, string? ContactEmail)>(
            $"SELECT ordinal, contact_name, contact_email FROM {PartTable} WHERE catalog_id = @id ORDER BY ordinal;",
            new { id = created.Id })).ToList();

        dbRows2.Should().HaveCount(1);
        dbRows2[0].Ordinal.Should().Be(1);
        dbRows2[0].ContactName.Should().Be("Carol");
    }

    [Fact]
    public async Task CreateAsync_RejectsUnknownFields_MissingRequiredFields_AndInvalidPartPayloads()
    {
        await EnsureTablesExistAsync(Fixture.ConnectionString);

        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICatalogService>();

        // Missing required field "name"
        await FluentActions.Awaiting(() => svc.CreateAsync(CatalogCode, new RecordPayload(new Dictionary<string, JsonElement>
            {
                ["email"] = JsonSerializer.SerializeToElement("x@y.com")
            }),
            CancellationToken.None))
            .Should()
            .ThrowAsync<NgbArgumentInvalidException>()
            .WithMessage("*Name is required.*");

        // Unknown field
        await FluentActions.Awaiting(() => svc.CreateAsync(CatalogCode, new RecordPayload(new Dictionary<string, JsonElement>
            {
                ["name"] = JsonSerializer.SerializeToElement("X"),
                ["nope"] = JsonSerializer.SerializeToElement("Y")
            }),
            CancellationToken.None))
            .Should()
            .ThrowAsync<NgbArgumentInvalidException>()
            .WithMessage("*Field*Nope*not available on this form*");

        // Unknown part
        await FluentActions.Awaiting(() => svc.CreateAsync(CatalogCode, new RecordPayload(
                Fields: new Dictionary<string, JsonElement> { ["name"] = JsonSerializer.SerializeToElement("X") },
                Parts: new Dictionary<string, RecordPartPayload>
                {
                    ["lines"] = new RecordPartPayload(new List<IReadOnlyDictionary<string, JsonElement>>
                    {
                        new Dictionary<string, JsonElement> { ["x"] = JsonSerializer.SerializeToElement(1) }
                    })
                }),
            CancellationToken.None))
            .Should()
            .ThrowAsync<NgbArgumentInvalidException>()
            .WithMessage("*Part*Lines*not available on this form*");

        // Managed technical key in part row
        await FluentActions.Awaiting(() => svc.CreateAsync(CatalogCode, new RecordPayload(
                Fields: new Dictionary<string, JsonElement> { ["name"] = JsonSerializer.SerializeToElement("Y") },
                Parts: new Dictionary<string, RecordPartPayload>
                {
                    [PartCode] = new RecordPartPayload(
                    [
                        new Dictionary<string, JsonElement>
                        {
                            ["catalog_id"] = JsonSerializer.SerializeToElement(Guid.CreateVersion7()),
                            ["ordinal"] = JsonSerializer.SerializeToElement(1),
                            ["contact_name"] = JsonSerializer.SerializeToElement("Alice"),
                        }
                    ])
                }),
            CancellationToken.None))
            .Should()
            .ThrowAsync<NgbArgumentInvalidException>()
            .WithMessage("*Catalog Id is managed automatically*");
    }

    private static async Task EnsureTablesExistAsync(string connectionString)
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

                  CREATE TABLE IF NOT EXISTS {PartTable} (
                      catalog_id     uuid NOT NULL,
                      ordinal        int  NOT NULL,
                      contact_name   text NOT NULL,
                      contact_email  text NULL,
                      extra_json     jsonb NULL,

                      CONSTRAINT fk_{PartTable}__catalog
                          FOREIGN KEY (catalog_id) REFERENCES catalogs(id)
                          ON DELETE CASCADE,
                      CONSTRAINT ux_{PartTable}__catalog_ordinal
                          UNIQUE (catalog_id, ordinal)
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
                        Indexes: []),
                    new CatalogTableMetadata(
                        TableName: PartTable,
                        Kind: TableKind.Part,
                        PartCode: PartCode,
                        Columns:
                        [
                            new("catalog_id", ColumnType.Guid, Required: true),
                            new("ordinal", ColumnType.Int32, Required: true),
                            new("contact_name", ColumnType.String, Required: true, MaxLength: 200),
                            new("contact_email", ColumnType.String, MaxLength: 200),
                            new("extra_json", ColumnType.Json),
                        ],
                        Indexes: [])
                ],
                Presentation: new CatalogPresentationMetadata(HeadTable, DisplayColumn),
                Version: new CatalogMetadataVersion(1, "integration-tests"))));
        }
    }
}
