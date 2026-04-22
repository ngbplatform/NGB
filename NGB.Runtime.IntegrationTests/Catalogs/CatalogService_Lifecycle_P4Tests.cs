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
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Catalogs;

[Collection(PostgresCollection.Name)]
public sealed class CatalogService_Lifecycle_P4Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string CatalogCode = "it_cat_universal";
    private const string HeadTable = "cat_it_cat_universal";
    private const string DisplayColumn = "name";

    [Fact]
    public async Task MarkForDeletionAsync_IsIdempotent_AndLookupExcludesDeleted_AndGetByIdShowsFlag()
    {
        await EnsureHeadTableExistsAsync(Fixture.ConnectionString);

        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICatalogService>();

        var created = await svc.CreateAsync(CatalogCode, new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement("Alpha")
        }), CancellationToken.None);

        await svc.MarkForDeletionAsync(CatalogCode, created.Id, CancellationToken.None);
        await svc.MarkForDeletionAsync(CatalogCode, created.Id, CancellationToken.None); // idempotent

        var read = await svc.GetByIdAsync(CatalogCode, created.Id, CancellationToken.None);
        read.IsMarkedForDeletion.Should().BeTrue();

        var lookup = await svc.LookupAsync(CatalogCode, query: null, limit: 10, CancellationToken.None);
        lookup.Should().BeEmpty();
    }

    [Fact]
    public async Task UnmarkForDeletionAsync_IsIdempotent_RestoresLookup_AndAllowsUpdate()
    {
        await EnsureHeadTableExistsAsync(Fixture.ConnectionString);

        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICatalogService>();

        var created = await svc.CreateAsync(CatalogCode, new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement("Alpha"),
            ["email"] = JsonSerializer.SerializeToElement("a@a.com")
        }), CancellationToken.None);

        await svc.MarkForDeletionAsync(CatalogCode, created.Id, CancellationToken.None);

        await svc.UnmarkForDeletionAsync(CatalogCode, created.Id, CancellationToken.None);
        await svc.UnmarkForDeletionAsync(CatalogCode, created.Id, CancellationToken.None); // idempotent

        var lookup = await svc.LookupAsync(CatalogCode, query: null, limit: 10, CancellationToken.None);
        lookup.Select(x => x.Id).Should().Equal(created.Id);
        lookup.Single().Label.Should().Be("Alpha");

        var updated = await svc.UpdateAsync(CatalogCode, created.Id, new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["email"] = JsonSerializer.SerializeToElement("b@b.com")
        }), CancellationToken.None);

        updated.IsMarkedForDeletion.Should().BeFalse();
        updated.Payload.Fields!["name"].GetString().Should().Be("Alpha");
        updated.Payload.Fields!["email"].GetString().Should().Be("b@b.com");
    }

    [Fact]
    public async Task LookupAsync_QueryIsCaseInsensitive_AndLimitIsRespected()
    {
        await EnsureHeadTableExistsAsync(Fixture.ConnectionString);

        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICatalogService>();

        await svc.CreateAsync(CatalogCode, new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement("Alpha")
        }), CancellationToken.None);

        await svc.CreateAsync(CatalogCode, new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement("Alpine")
        }), CancellationToken.None);

        await svc.CreateAsync(CatalogCode, new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement("Beta")
        }), CancellationToken.None);

        var lookup = await svc.LookupAsync(CatalogCode, query: "AL", limit: 1, CancellationToken.None);
        lookup.Should().HaveCount(1);
        lookup.Single().Label.Should().Be("Alpha");
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
