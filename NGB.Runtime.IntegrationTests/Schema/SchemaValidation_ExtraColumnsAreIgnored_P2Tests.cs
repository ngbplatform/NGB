using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
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

[Collection(PostgresCollection.Name)]
public sealed class SchemaValidation_ExtraColumnsAreIgnored_P2Tests(PostgresTestFixture fixture)
{
    private const string DocTypeCode = "it_doc_extra";
    private const string CatalogCode = "it_cat_extra";

    [Fact]
    public async Task DocumentSchemaValidation_WhenExtraColumnsExist_Passes()
    {
        await fixture.ResetDatabaseAsync();

        await ResetTypedSchemaAsync(fixture.ConnectionString);
        await CreateTypedDocumentSchema_WithExtraColumnsAsync(fixture.ConnectionString);

        using var host = CreateHostWithRegistries(fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<IDocumentSchemaValidationService>();

        await validator.Invoking(v => v.ValidateAllAsync(CancellationToken.None))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task CatalogSchemaValidation_WhenExtraColumnsExist_Passes()
    {
        await fixture.ResetDatabaseAsync();

        await ResetTypedSchemaAsync(fixture.ConnectionString);
        await CreateTypedCatalogSchema_WithExtraColumnsAsync(fixture.ConnectionString);

        using var host = CreateHostWithRegistries(fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<ICatalogSchemaValidationService>();

        await validator.Invoking(v => v.ValidateAllAsync(CancellationToken.None))
            .Should().NotThrowAsync();
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
                    TableName: "doc_it_doc_extra",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("counterparty_id", ColumnType.Guid),
                        new("total_amount", ColumnType.Decimal),
                    ],
                    Indexes:
                    [
                        new DocumentIndexMetadata("ix_doc_it_doc_extra__counterparty", ["counterparty_id"]),
                    ]),
                new DocumentTableMetadata(
                    TableName: "doc_it_doc_extra__lines",
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
                        new DocumentIndexMetadata("ux_doc_it_doc_extra__lines", ["document_id", "line_no"], Unique: true),
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
                    TableName: "cat_it_cat_extra",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("catalog_id", ColumnType.Guid, Required: true),
                        new("name", ColumnType.String),
                    ],
                    Indexes: []),
            ],
            Presentation: new CatalogPresentationMetadata("cat_it_cat_extra", "name"),
            Version: new CatalogMetadataVersion(1, "integration-tests")
        ));

        return reg;
    }

    private static async Task ResetTypedSchemaAsync(string connectionString)
    {
        const string sql = """
        DROP INDEX IF EXISTS ix_doc_it_doc_extra__counterparty;
        DROP INDEX IF EXISTS ux_doc_it_doc_extra__lines;

        DROP TABLE IF EXISTS doc_it_doc_extra__lines;
        DROP TABLE IF EXISTS doc_it_doc_extra;
        DROP TABLE IF EXISTS cat_it_cat_extra;
        """;

        await ExecuteAsync(connectionString, sql);
    }

    private static async Task CreateTypedDocumentSchema_WithExtraColumnsAsync(string connectionString)
    {
        const string sql = """
        CREATE TABLE IF NOT EXISTS doc_it_doc_extra (
            document_id      uuid PRIMARY KEY,
            counterparty_id  uuid NULL,
            total_amount     numeric(18,2) NULL,

            -- extra column not present in metadata
            extra_note       text NULL,

            CONSTRAINT fk_doc_it_doc_extra__document
                FOREIGN KEY (document_id) REFERENCES documents(id)
                ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS doc_it_doc_extra__lines (
            document_id  uuid NOT NULL,
            line_no      int  NOT NULL,
            amount       numeric(18,2) NULL,

            -- extra columns not present in metadata
            extra_json   jsonb NULL,

            CONSTRAINT pk_doc_it_doc_extra__lines
                PRIMARY KEY (document_id, line_no),

            CONSTRAINT fk_doc_it_doc_extra__lines__document
                FOREIGN KEY (document_id) REFERENCES documents(id)
                ON DELETE CASCADE
        );

        CREATE INDEX IF NOT EXISTS ix_doc_it_doc_extra__counterparty
            ON doc_it_doc_extra(counterparty_id);

        CREATE UNIQUE INDEX IF NOT EXISTS ux_doc_it_doc_extra__lines
            ON doc_it_doc_extra__lines(document_id, line_no);
        """;

        await ExecuteAsync(connectionString, sql);
    }

    private static async Task CreateTypedCatalogSchema_WithExtraColumnsAsync(string connectionString)
    {
        const string sql = """
        CREATE TABLE IF NOT EXISTS cat_it_cat_extra (
            catalog_id uuid PRIMARY KEY REFERENCES catalogs(id) ON DELETE CASCADE,
            name       text NULL,

            -- extra columns not present in metadata
            tags       text[] NULL,
            meta       jsonb NULL
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
