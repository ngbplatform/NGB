using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.Dimensions.Enrichment;
using NGB.Definitions;
using NGB.Metadata.Base;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Metadata.Documents.Hybrid;
using NGB.Persistence.Dimensions.Enrichment;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Extensions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

[Collection(PostgresCollection.Name)]
public sealed class DimensionValueEnrichment_CatalogRegistered_MissingValues_Fallbacks_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string DimensionCode = "it_cat_enrich";
    private const string CatalogTable = "cat_it_cat_enrich";
    private const string TypedDocumentTypeCode = "it_doc_display";
    private const string TypedDocumentHeadTable = "doc_it_doc_display";

    [Fact]
    public async Task ResolveAsync_FallsBackToDocument_WhenCatalogIsRegisteredButValueMissingFromCatalog()
    {
        using var host = CreateHostWithTestCatalog(Fixture.ConnectionString);

        var dimensionId = GetDimensionId();

        await EnsureDimensionAsync(Fixture.ConnectionString, dimensionId, DimensionCode, name: "IT Catalog Enrich");
        await EnsureCatalogTableAsync(Fixture.ConnectionString);

        var valueId = Guid.CreateVersion7();
        await SeedDocumentAsync(Fixture.ConnectionString, valueId, typeCode: "it_doc", number: "DOC-42");

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IDimensionValueEnrichmentReader>();

        var key = new DimensionValueKey(dimensionId, valueId);

        var result = await reader.ResolveAsync(new[] { key }, CancellationToken.None);

        result.Should().ContainKey(key);
        result[key].Should().Be("it_doc DOC-42");
    }

    [Fact]
    public async Task ResolveAsync_UsesTypedDocumentDisplay_WhenTypedHeadDefinesDisplay()
    {
        using var host = CreateHostWithTestCatalog(Fixture.ConnectionString);

        var dimensionId = GetDimensionId();

        await EnsureDimensionAsync(Fixture.ConnectionString, dimensionId, DimensionCode, name: "IT Catalog Enrich");
        await EnsureCatalogTableAsync(Fixture.ConnectionString);
        await EnsureTypedDocumentHeadTableAsync(Fixture.ConnectionString);

        var valueId = Guid.CreateVersion7();
        await SeedDocumentAsync(Fixture.ConnectionString, valueId, typeCode: TypedDocumentTypeCode, number: "DOC-LEASE");
        await SeedTypedDocumentHeadAsync(Fixture.ConnectionString, valueId, "Lease L-001 — Unit 09");

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IDimensionValueEnrichmentReader>();

        var key = new DimensionValueKey(dimensionId, valueId);

        var result = await reader.ResolveAsync(new[] { key }, CancellationToken.None);

        result.Should().ContainKey(key);
        result[key].Should().Be("Lease L-001 — Unit 09");
    }

    [Fact]
    public async Task ResolveAsync_FallsBackToShortGuid_WhenCatalogIsRegisteredButValueMissingAndNotDocument()
    {
        using var host = CreateHostWithTestCatalog(Fixture.ConnectionString);

        var dimensionId = GetDimensionId();

        await EnsureDimensionAsync(Fixture.ConnectionString, dimensionId, DimensionCode, name: "IT Catalog Enrich");
        await EnsureCatalogTableAsync(Fixture.ConnectionString);

        var valueId = Guid.CreateVersion7();
        var expected = valueId.ToString("N")[..8];

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IDimensionValueEnrichmentReader>();

        var key = new DimensionValueKey(dimensionId, valueId);

        var result = await reader.ResolveAsync(new[] { key }, CancellationToken.None);

        result[key].Should().Be(expected);
    }

    [Fact]
    public async Task ResolveAsync_UsesCatalogForKnownIds_AndFallsBackForMissingIds_InSameBatch()
    {
        using var host = CreateHostWithTestCatalog(Fixture.ConnectionString);

        var dimensionId = GetDimensionId();

        await EnsureDimensionAsync(Fixture.ConnectionString, dimensionId, DimensionCode, name: "IT Catalog Enrich");
        await EnsureCatalogTableAsync(Fixture.ConnectionString);

        var catalogValueId = Guid.CreateVersion7();
        await SeedCatalogValueAsync(Fixture.ConnectionString, catalogValueId, "Cat A");

        var documentValueId = Guid.CreateVersion7();
        await SeedDocumentAsync(Fixture.ConnectionString, documentValueId, typeCode: "it_doc", number: "DOC-B");

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IDimensionValueEnrichmentReader>();

        var keyCatalog = new DimensionValueKey(dimensionId, catalogValueId);
        var keyDoc = new DimensionValueKey(dimensionId, documentValueId);

        var result = await reader.ResolveAsync(new[] { keyCatalog, keyDoc }, CancellationToken.None);

        result[keyCatalog].Should().Be("Cat A");
        result[keyDoc].Should().Be("it_doc DOC-B");
    }

    [Fact]
    public async Task ResolveAsync_PrefersCatalogOverDocument_WhenBothExist()
    {
        using var host = CreateHostWithTestCatalog(Fixture.ConnectionString);

        var dimensionId = GetDimensionId();

        await EnsureDimensionAsync(Fixture.ConnectionString, dimensionId, DimensionCode, name: "IT Catalog Enrich");
        await EnsureCatalogTableAsync(Fixture.ConnectionString);

        var valueId = Guid.CreateVersion7();
        await SeedCatalogValueAsync(Fixture.ConnectionString, valueId, "Cat Wins");
        await SeedDocumentAsync(Fixture.ConnectionString, valueId, typeCode: "it_doc", number: "DOC-999");

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IDimensionValueEnrichmentReader>();

        var key = new DimensionValueKey(dimensionId, valueId);

        var result = await reader.ResolveAsync(new[] { key }, CancellationToken.None);

        result[key].Should().Be("Cat Wins");
    }

    private static IHost CreateHostWithTestCatalog(string connectionString)
    {
        return IntegrationHostFactory.Create(connectionString, services =>
        {
            services.AddSingleton<IDefinitionsContributor, TestCatalogContributor>();
        });
    }

    private static Guid GetDimensionId()
    {
        var codeNorm = DimensionCode.Trim().ToLowerInvariant();
        return DeterministicGuid.Create($"Dimension|{codeNorm}");
    }

    private static async Task EnsureDimensionAsync(string connectionString, Guid dimensionId, string code, string name)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(CancellationToken.None);

        const string sql = """
                           INSERT INTO platform_dimensions(
                               dimension_id,
                               code,
                               name,
                               is_active,
                               is_deleted,
                               created_at_utc,
                               updated_at_utc
                           )
                           VALUES (
                               @dimensionId,
                               @code,
                               @name,
                               TRUE,
                               FALSE,
                               (NOW() AT TIME ZONE 'UTC'),
                               (NOW() AT TIME ZONE 'UTC')
                           )
                           ON CONFLICT (dimension_id) DO NOTHING;
                           """;

        await conn.ExecuteAsync(sql, new { dimensionId, code = code.Trim(), name = name.Trim() });
    }

    private static async Task EnsureCatalogTableAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(CancellationToken.None);

        var sql = $"""
                  CREATE TABLE IF NOT EXISTS {CatalogTable} (
                      catalog_id UUID PRIMARY KEY,
                      name TEXT NULL
                  );
                  """;

        await conn.ExecuteAsync(sql);
    }


    private static async Task EnsureTypedDocumentHeadTableAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(CancellationToken.None);

        var sql = $"""
                  CREATE TABLE IF NOT EXISTS {TypedDocumentHeadTable} (
                      document_id UUID PRIMARY KEY REFERENCES documents(id) ON DELETE CASCADE,
                      display TEXT NULL
                  );
                  """;

        await conn.ExecuteAsync(sql);
    }

    private static async Task SeedTypedDocumentHeadAsync(string connectionString, Guid id, string display)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(CancellationToken.None);

        var sql = $"""
                  INSERT INTO {TypedDocumentHeadTable}(document_id, display)
                  VALUES (@id, @display);
                  """;

        await conn.ExecuteAsync(sql, new { id, display });
    }

    private static async Task SeedCatalogValueAsync(string connectionString, Guid id, string name)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(CancellationToken.None);

        var sql = $"""
                  INSERT INTO {CatalogTable}(catalog_id, name)
                  VALUES (@id, @name);
                  """;

        await conn.ExecuteAsync(sql, new { id, name });
    }

    private static async Task SeedDocumentAsync(string connectionString, Guid id, string typeCode, string number)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(CancellationToken.None);

        var dateUtc = ReportingTestHelpers.Day15Utc;

        const string sql = """
                           INSERT INTO documents (
                               id,
                               type_code,
                               number,
                               date_utc,
                               status,
                               posted_at_utc,
                               marked_for_deletion_at_utc,
                               created_at_utc,
                               updated_at_utc
                           )
                           VALUES (
                               @id,
                               @typeCode,
                               @number,
                               @dateUtc,
                               1,
                               NULL,
                               NULL,
                               @dateUtc,
                               @dateUtc
                           );
                           """;

        await conn.ExecuteAsync(sql, new { id, typeCode, number, dateUtc });
    }

    private sealed class TestCatalogContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddCatalog(DimensionCode, c => c.Metadata(new CatalogTypeMetadata(
                CatalogCode: DimensionCode,
                DisplayName: "IT Test Catalog",
                Tables: Array.Empty<CatalogTableMetadata>(),
                Presentation: new CatalogPresentationMetadata(CatalogTable, "name"),
                Version: new CatalogMetadataVersion(1, "tests"))));

            builder.AddDocument(TypedDocumentTypeCode, d => d.Metadata(new DocumentTypeMetadata(
                TypeCode: TypedDocumentTypeCode,
                Tables:
                [
                    new DocumentTableMetadata(
                        TableName: TypedDocumentHeadTable,
                        Kind: TableKind.Head,
                        Columns:
                        [
                            new DocumentColumnMetadata("display", ColumnType.String)
                        ])
                ],
                Presentation: new DocumentPresentationMetadata("Lease"),
                Version: new DocumentMetadataVersion(1, "tests"))));
        }
    }
}
