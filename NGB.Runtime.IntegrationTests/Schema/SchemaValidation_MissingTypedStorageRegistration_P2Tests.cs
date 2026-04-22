using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NGB.Core.Catalogs.Exceptions;
using NGB.Metadata.Base;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Metadata.Catalogs.Storage;
using NGB.Metadata.Documents.Hybrid;
using NGB.Metadata.Documents.Storage;
using NGB.Runtime.Catalogs;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Schema;

/// <summary>
/// P2-9: schema validation checks the physical schema only.
/// Storage bindings are validated separately by startup Definitions validation.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SchemaValidation_MissingTypedStorageRegistration_P2Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string DocTypeCode = "it_doc";
    private const string CatalogCode = "it_cat";

    [Fact]
    public async Task DocumentSchemaValidation_WhenTypeStorageMissing_ButSchemaMatches_Passes()
    {
        await CreateTypedDocumentSchemaAsync(Fixture.ConnectionString);

        using var host = CreateHostWithRegistriesOnly(Fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<IDocumentSchemaValidationService>();

        await validator.Invoking(v => v.ValidateAllAsync(CancellationToken.None))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task CatalogSchemaValidation_WhenTypeStorageMissing_Throws()
    {
        await CreateTypedCatalogSchemaAsync(Fixture.ConnectionString);

        using var host = CreateHostWithRegistriesOnly(Fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<ICatalogSchemaValidationService>();

        await validator.Invoking(v => v.ValidateAllAsync(CancellationToken.None))
            .Should().ThrowAsync<CatalogSchemaValidationException>()
            .WithMessage("*missing ICatalogTypeStorage registration*");
    }

    private static IHost CreateHostWithRegistriesOnly(string connectionString)
    {
        return IntegrationHostFactory.Create(
            connectionString,
            configureTestServices: services =>
            {
                services.RemoveAll<IDocumentTypeRegistry>();
                services.RemoveAll<ICatalogTypeRegistry>();

                services.AddSingleton<IDocumentTypeRegistry>(_ => BuildDocumentRegistry());
                services.AddSingleton<ICatalogTypeRegistry>(_ => BuildCatalogRegistry());

                // Intentionally DO NOT register IDocumentTypeStorage / ICatalogTypeStorage.
            });
    }

    private static IDocumentTypeRegistry BuildDocumentRegistry()
    {
        var reg = new DocumentTypeRegistry();

        reg.Register(new DocumentTypeMetadata(
            TypeCode: DocTypeCode,
            Tables:
            [
                new DocumentTableMetadata(
                    TableName: "doc_it_doc",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("memo", ColumnType.String),
                    ],
                    Indexes: [])
            ],
            Presentation: new DocumentPresentationMetadata("Integration Test Doc"),
            Version: new DocumentMetadataVersion(1, "integration-tests")
        ));

        return reg;
    }

    private static ICatalogTypeRegistry BuildCatalogRegistry()
    {
        var reg = new CatalogTypeRegistry();

        reg.Register(new CatalogTypeMetadata(
            CatalogCode: CatalogCode,
            DisplayName: "Integration Test Catalog",
            Tables:
            [
                new CatalogTableMetadata(
                    TableName: "cat_it_cat",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("catalog_id", ColumnType.Guid, Required: true),
                        new("name", ColumnType.String),
                    ],
                    Indexes: [])
            ],
            Presentation: new CatalogPresentationMetadata("cat_it_cat", "name"),
            Version: new CatalogMetadataVersion(1, "integration-tests")
        ));

        return reg;
    }

    private static async Task CreateTypedDocumentSchemaAsync(string connectionString)
    {
        const string sql = """
        DROP TABLE IF EXISTS doc_it_doc;

        CREATE TABLE IF NOT EXISTS doc_it_doc (
            document_id uuid PRIMARY KEY,
            memo        text NULL,

            CONSTRAINT fk_doc_it_doc__document
                FOREIGN KEY (document_id) REFERENCES documents(id)
                ON DELETE CASCADE
        );
        """;

        await ExecuteAsync(connectionString, sql);
    }

    private static async Task CreateTypedCatalogSchemaAsync(string connectionString)
    {
        const string sql = """
        DROP TABLE IF EXISTS cat_it_cat;

        CREATE TABLE IF NOT EXISTS cat_it_cat (
            catalog_id uuid PRIMARY KEY,
            name       text NULL,

            CONSTRAINT fk_cat_it_cat__catalog
                FOREIGN KEY (catalog_id) REFERENCES catalogs(id)
                ON DELETE CASCADE
        );
        """;

        await ExecuteAsync(connectionString, sql);
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(CancellationToken.None);

        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(CancellationToken.None);
    }
}
