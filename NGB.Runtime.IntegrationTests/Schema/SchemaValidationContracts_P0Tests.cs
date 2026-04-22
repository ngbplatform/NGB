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
using NGB.Core.Documents.Exceptions;
using NGB.Runtime.Catalogs;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Schema;

[Collection(PostgresCollection.Name)]
public sealed class SchemaValidationContracts_P0Tests(PostgresTestFixture fixture)
{
    private const string DocTypeCode = "it_doc";
    private const string CatalogCode = "it_cat";

    [Fact]
    public async Task DocumentSchemaValidation_WithMatchingSchema_Passes()
    {
        await fixture.ResetDatabaseAsync();

        await ResetTypedSchemaAsync(fixture.ConnectionString);
        await CreateTypedDocumentSchemaAsync(fixture.ConnectionString, createAllIndexes: true);

        using var host = CreateHostWithRegistries(fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<IDocumentSchemaValidationService>();

        await validator.Invoking(v => v.ValidateAllAsync(CancellationToken.None))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task DocumentSchemaValidation_WhenRequiredIndexMissing_ThrowsWithSpecificError()
    {
        await fixture.ResetDatabaseAsync();

        await ResetTypedSchemaAsync(fixture.ConnectionString);
        await CreateTypedDocumentSchemaAsync(fixture.ConnectionString, createAllIndexes: false);

        using var host = CreateHostWithRegistries(fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<IDocumentSchemaValidationService>();

        await validator.Invoking(v => v.ValidateAllAsync(CancellationToken.None))
            .Should().ThrowAsync<DocumentSchemaValidationException>()
            .WithMessage("*doc_it_doc*missing index 'ix_doc_it_doc__counterparty'*");
    }

    [Fact]
    public async Task CatalogSchemaValidation_WithMatchingSchema_Passes()
    {
        await fixture.ResetDatabaseAsync();

        await ResetTypedSchemaAsync(fixture.ConnectionString);
        await CreateTypedCatalogSchemaAsync(fixture.ConnectionString, includeFkToCatalogs: true);

        using var host = CreateHostWithRegistries(fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<ICatalogSchemaValidationService>();

        await validator.Invoking(v => v.ValidateAllAsync(CancellationToken.None))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task CatalogSchemaValidation_WhenCatalogIdFkMissing_Throws()
    {
        await fixture.ResetDatabaseAsync();

        await ResetTypedSchemaAsync(fixture.ConnectionString);
        await CreateTypedCatalogSchemaAsync(fixture.ConnectionString, includeFkToCatalogs: false);

        using var host = CreateHostWithRegistries(fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<ICatalogSchemaValidationService>();

        await validator.Invoking(v => v.ValidateAllAsync(CancellationToken.None))
            .Should().ThrowAsync<CatalogSchemaValidationException>()
            .WithMessage("*Catalog 'it_cat'*FK catalog_id -> catalogs(id)*");
    }

    private static IHost CreateHostWithRegistries(string connectionString)
    {
        return IntegrationHostFactory.Create(
            connectionString,
            configureTestServices: services =>
            {
                services.RemoveAll<IDocumentTypeRegistry>();
                services.RemoveAll<ICatalogTypeRegistry>();

                services.AddSingleton<IDocumentTypeRegistry>(_ => BuildDocumentRegistry());
                services.AddSingleton<ICatalogTypeRegistry>(_ => BuildCatalogRegistry());

                services.AddNoopDocumentTypeStorage(DocTypeCode);
                services.AddNoopCatalogTypeStorage(CatalogCode);
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
                        new("counterparty_id", ColumnType.Guid),
                        new("total_amount", ColumnType.Decimal),
                    ],
                    Indexes:
                    [
                        new DocumentIndexMetadata("ix_doc_it_doc__counterparty", ["counterparty_id"]),
                    ]),
                new DocumentTableMetadata(
                    TableName: "doc_it_doc__lines",
                    Kind: TableKind.Part,
                    PartCode: "lines",
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("line_no", ColumnType.Int32, Required: true),
                        new("amount", ColumnType.Decimal),
                    ],
                    Indexes:
                    [
                        new DocumentIndexMetadata("ux_doc_it_doc__lines", ["document_id", "line_no"], Unique: true),
                    ]),
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
                    Indexes: []),
            ],
            Presentation: new CatalogPresentationMetadata("cat_it_cat", "name"),
            Version: new CatalogMetadataVersion(1, "integration-tests")
        ));

        return reg;
    }

    private static async Task ResetTypedSchemaAsync(string connectionString)
    {
        const string sql = """
        DROP INDEX IF EXISTS ix_doc_it_doc__counterparty;
        DROP INDEX IF EXISTS ux_doc_it_doc__lines;

        DROP TABLE IF EXISTS doc_it_doc__lines;
        DROP TABLE IF EXISTS doc_it_doc;

        DROP TABLE IF EXISTS cat_it_cat;
        """;

        await ExecuteAsync(connectionString, sql);
    }

    private static async Task CreateTypedDocumentSchemaAsync(string connectionString, bool createAllIndexes)
    {
        // NOTE:
        // We explicitly create the UNIQUE index expected by metadata even though PK already guarantees uniqueness.
        // Schema validation checks index NAMES, not just logical uniqueness.

        var sql = """
        CREATE TABLE IF NOT EXISTS doc_it_doc (
            document_id      uuid PRIMARY KEY,
            counterparty_id  uuid NULL,
            total_amount     numeric(18,2) NULL,

            CONSTRAINT fk_doc_it_doc__document
                FOREIGN KEY (document_id) REFERENCES documents(id)
                ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS doc_it_doc__lines (
            document_id  uuid NOT NULL,
            line_no      int  NOT NULL,
            amount       numeric(18,2) NULL,

            CONSTRAINT pk_doc_it_doc__lines
                PRIMARY KEY (document_id, line_no),

            CONSTRAINT fk_doc_it_doc__lines__document
                FOREIGN KEY (document_id) REFERENCES documents(id)
                ON DELETE CASCADE
        );

        CREATE UNIQUE INDEX IF NOT EXISTS ux_doc_it_doc__lines
            ON doc_it_doc__lines(document_id, line_no);
        """;

        await ExecuteAsync(connectionString, sql);

        if (createAllIndexes)
        {
            await ExecuteAsync(connectionString, """
            CREATE INDEX IF NOT EXISTS ix_doc_it_doc__counterparty
                ON doc_it_doc(counterparty_id);
            """);
        }
        else
        {
            // Make test deterministic even if the index exists from a previous run.
            await ExecuteAsync(connectionString, "DROP INDEX IF EXISTS ix_doc_it_doc__counterparty;");
        }
    }

    private static async Task CreateTypedCatalogSchemaAsync(string connectionString, bool includeFkToCatalogs)
    {
        // catalog_id must exist, be NOT NULL, and reference catalogs(id).
        var sql = includeFkToCatalogs
            ? """
              CREATE TABLE IF NOT EXISTS cat_it_cat (
                  catalog_id uuid PRIMARY KEY REFERENCES catalogs(id) ON DELETE CASCADE,
                  name       text NULL
              );
              """
            : """
              CREATE TABLE IF NOT EXISTS cat_it_cat (
                  catalog_id uuid PRIMARY KEY,
                  name       text NULL
              );
              """;

        await ExecuteAsync(connectionString, sql);
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }
}
