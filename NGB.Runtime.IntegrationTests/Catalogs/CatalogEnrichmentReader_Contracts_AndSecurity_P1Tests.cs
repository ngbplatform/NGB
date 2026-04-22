using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Catalogs.Exceptions;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Metadata.Catalogs.Storage;
using NGB.Persistence.Catalogs.Enrichment;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Catalogs;

[Collection(PostgresCollection.Name)]
public sealed class CatalogEnrichmentReader_Contracts_AndSecurity_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string CatalogCode = "it_cat_enrich";
    private const string TableName = "cat_it_cat_enrich";
    private const string DisplayColumn = "name";
    private const string SecondCatalogCode = "it_cat_enrich_two";
    private const string SecondTableName = "cat_it_cat_enrich_two";

    [Fact]
    public async Task ResolveAsync_EmptyIds_ReturnsEmpty_AndDoesNotRequireMetadata()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<ICatalogTypeRegistry, CatalogTypeRegistry>();
            });

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<ICatalogEnrichmentReader>();

        // Empty ids must short-circuit BEFORE any metadata lookup.
        var result = await reader.ResolveAsync("unknown_code", Array.Empty<Guid>(), CancellationToken.None);

        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_UnknownCatalogCode_Throws()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<ICatalogTypeRegistry, CatalogTypeRegistry>();
            });

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<ICatalogEnrichmentReader>();

        var act = () => reader.ResolveAsync("unknown_code", new[] { Guid.CreateVersion7() }, CancellationToken.None);

        await act.Should().ThrowAsync<CatalogTypeNotFoundException>()
            .WithMessage("*Unknown catalog code*unknown_code*");
    }

    [Fact]
    public async Task ResolveAsync_WithUnsafePresentationMetadata_Throws()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<ICatalogTypeRegistry, CatalogTypeRegistry>();
            });

        await using var scope = host.Services.CreateAsyncScope();

        var registry = scope.ServiceProvider.GetRequiredService<ICatalogTypeRegistry>();
        registry.Register(new CatalogTypeMetadata(
            CatalogCode: "it_cat_bad",
            DisplayName: "Bad",
            Tables: Array.Empty<CatalogTableMetadata>(),
            Presentation: new CatalogPresentationMetadata(TableName: "cat_bad;drop_table", DisplayColumn: "name"),
            Version: new CatalogMetadataVersion(1, "it")));

        var reader = scope.ServiceProvider.GetRequiredService<ICatalogEnrichmentReader>();

        var act = () => reader.ResolveAsync("it_cat_bad", new[] { Guid.CreateVersion7() }, CancellationToken.None);

        await act.Should().ThrowAsync<CatalogPresentationMetadataUnsafeIdentifierException>()
            .WithMessage("*Unsafe table/column identifier*it_cat_bad*");
    }

    [Fact]
    public async Task ResolveAsync_WithDuplicatesAndNullDisplay_ReturnsUniqueKeys_AndMapsNullToEmptyString()
    {
        await EnsureTypedTableExistsAsync(Fixture.ConnectionString, TableName, DisplayColumn);

        var id1 = Guid.CreateVersion7();
        var id2 = Guid.CreateVersion7();
        var missing = Guid.CreateVersion7();

        await SeedAsync(Fixture.ConnectionString, TableName, DisplayColumn, (id1, "First"), (id2, null));

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<ICatalogTypeRegistry, CatalogTypeRegistry>();
            });

        await using var scope = host.Services.CreateAsyncScope();

        var registry = scope.ServiceProvider.GetRequiredService<ICatalogTypeRegistry>();
        registry.Register(new CatalogTypeMetadata(
            CatalogCode: CatalogCode,
            DisplayName: "IT Catalog",
            Tables: Array.Empty<CatalogTableMetadata>(),
            Presentation: new CatalogPresentationMetadata(TableName, DisplayColumn),
            Version: new CatalogMetadataVersion(1, "it")));

        var reader = scope.ServiceProvider.GetRequiredService<ICatalogEnrichmentReader>();

        var ids = new[] { id1, id1, id2, missing };
        var result = await reader.ResolveAsync(CatalogCode, ids, CancellationToken.None);

        result.Should().HaveCount(2);
        result.Should().ContainKey(id1);
        result[id1].Should().Be("First");

        result.Should().ContainKey(id2);
        result[id2].Should().Be(string.Empty);

        result.Should().NotContainKey(missing);
    }

    [Fact]
    public async Task ResolveManyAsync_MixedCatalogBatchAcrossTables_ReturnsNestedMaps()
    {
        await EnsureTypedTableExistsAsync(Fixture.ConnectionString, TableName, DisplayColumn);
        await EnsureTypedTableExistsAsync(Fixture.ConnectionString, SecondTableName, DisplayColumn);

        var firstId = Guid.CreateVersion7();
        var secondId = Guid.CreateVersion7();
        var thirdId = Guid.CreateVersion7();
        var missing = Guid.CreateVersion7();

        await SeedAsync(Fixture.ConnectionString, TableName, DisplayColumn, (firstId, "First"), (secondId, null));
        await SeedAsync(Fixture.ConnectionString, SecondTableName, DisplayColumn, (thirdId, "Third"));

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<ICatalogTypeRegistry, CatalogTypeRegistry>();
            });

        await using var scope = host.Services.CreateAsyncScope();

        var registry = scope.ServiceProvider.GetRequiredService<ICatalogTypeRegistry>();
        registry.Register(new CatalogTypeMetadata(
            CatalogCode: CatalogCode,
            DisplayName: "IT Catalog",
            Tables: Array.Empty<CatalogTableMetadata>(),
            Presentation: new CatalogPresentationMetadata(TableName, DisplayColumn),
            Version: new CatalogMetadataVersion(1, "it")));
        registry.Register(new CatalogTypeMetadata(
            CatalogCode: SecondCatalogCode,
            DisplayName: "IT Catalog 2",
            Tables: Array.Empty<CatalogTableMetadata>(),
            Presentation: new CatalogPresentationMetadata(SecondTableName, DisplayColumn),
            Version: new CatalogMetadataVersion(1, "it")));

        var reader = scope.ServiceProvider.GetRequiredService<ICatalogEnrichmentReader>();

        var result = await reader.ResolveManyAsync(
            new Dictionary<string, IReadOnlyCollection<Guid>>(StringComparer.OrdinalIgnoreCase)
            {
                [CatalogCode] = [firstId, secondId, missing],
                [SecondCatalogCode] = [thirdId, thirdId]
            },
            CancellationToken.None);

        result.Should().HaveCount(2);
        result[CatalogCode].Should().BeEquivalentTo(new Dictionary<Guid, string>
        {
            [firstId] = "First",
            [secondId] = string.Empty
        });
        result[SecondCatalogCode].Should().BeEquivalentTo(new Dictionary<Guid, string>
        {
            [thirdId] = "Third"
        });
    }

    private static async Task EnsureTypedTableExistsAsync(string connectionString, string tableName, string displayColumn)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(CancellationToken.None);

        var sql = $"""
                  CREATE TABLE IF NOT EXISTS {tableName} (
                      catalog_id uuid PRIMARY KEY,
                      {displayColumn} text NULL
                  );
                  """;

        await conn.ExecuteAsync(sql);
    }

    private static async Task SeedAsync(
        string connectionString,
        string tableName,
        string displayColumn,
        params (Guid Id, string? Display)[] rows)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(CancellationToken.None);

        foreach (var row in rows)
        {
            var sql = $"""
                      INSERT INTO {tableName} (catalog_id, {displayColumn})
                      VALUES (@id, @display)
                      ON CONFLICT (catalog_id) DO UPDATE SET {displayColumn} = EXCLUDED.{displayColumn};
                      """;

            await conn.ExecuteAsync(sql, new { id = row.Id, display = row.Display });
        }
    }
}
