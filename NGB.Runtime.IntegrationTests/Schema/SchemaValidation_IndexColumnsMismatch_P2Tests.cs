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
using NGB.Core.Documents.Exceptions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Schema;

[Collection(PostgresCollection.Name)]
public sealed class SchemaValidation_IndexColumnsMismatch_P2Tests(PostgresTestFixture fixture)
{
    private const string DocTypeCode = "it_doc_ix_m";
    private const string CatalogCode = "it_cat_ix_m";

    [Fact]
    public async Task DocumentSchemaValidation_WhenIndexColumnsMismatch_ThrowsWithSpecificErrors()
    {
        await fixture.ResetDatabaseAsync();

        await ResetTypedSchemaAsync(fixture.ConnectionString);
        await CreateTypedDocumentSchema_WithMismatchedIndexesAsync(fixture.ConnectionString);

        using var host = CreateHostWithRegistries(fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<IDocumentSchemaValidationService>();

        var ex = await validator.Invoking(v => v.ValidateAllAsync(CancellationToken.None))
            .Should().ThrowAsync<DocumentSchemaValidationException>();

        ex.Which.Message.Should().Contain("Document schema validation failed");
        ex.Which.Message.Should().Contain("index 'ix_doc_it_doc_ix_m__counterparty' columns mismatch");
        ex.Which.Message.Should().Contain("Expected [counterparty_id], got [total_amount]");
        ex.Which.Message.Should().Contain("index 'ux_doc_it_doc_ix_m__lines' columns mismatch");
        ex.Which.Message.Should().Contain("Expected [document_id, line_no], got [line_no, document_id]");
    }

    [Fact]
    public async Task CatalogSchemaValidation_WhenIndexColumnsMismatch_ReportsWarning_ButDoesNotThrow()
    {
        await fixture.ResetDatabaseAsync();

        await ResetTypedSchemaAsync(fixture.ConnectionString);
        await CreateTypedCatalogSchema_WithMismatchedIndexAsync(fixture.ConnectionString);

        using var host = CreateHostWithRegistries(fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<ICatalogSchemaValidationService>();

        // Catalog schema validator treats index checks as best-effort diagnostics (warnings), not errors.
        var diag = await validator.DiagnoseAllAsync(CancellationToken.None);

        diag.Errors.Should().BeEmpty();
        diag.Warnings.Should().NotBeEmpty();
        diag.ToString().Should().Contain("index 'ix_cat_it_cat_ix_m__name' columns mismatch");
        diag.ToString().Should().Contain("Expected [name], got [catalog_id]");

        var act = () => validator.ValidateAllAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();
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
                    TableName: "doc_it_doc_ix_m",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("counterparty_id", ColumnType.Guid),
                        new("total_amount", ColumnType.Decimal),
                    ],
                    Indexes:
                    [
                        new DocumentIndexMetadata("ix_doc_it_doc_ix_m__counterparty", ["counterparty_id"]),
                    ]),
                new DocumentTableMetadata(
                    TableName: "doc_it_doc_ix_m__lines",
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
                        new DocumentIndexMetadata("ux_doc_it_doc_ix_m__lines", ["document_id", "line_no"], Unique: true),
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
                    TableName: "cat_it_cat_ix_m",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("catalog_id", ColumnType.Guid, Required: true),
                        new("name", ColumnType.String),
                    ],
                    Indexes:
                    [
                        new CatalogIndexMetadata("ix_cat_it_cat_ix_m__name", ["name"]),
                    ]),
            ],
            Presentation: new CatalogPresentationMetadata("cat_it_cat_ix_m", "name"),
            Version: new CatalogMetadataVersion(1, "integration-tests")
        ));

        return reg;
    }

    private static async Task ResetTypedSchemaAsync(string connectionString)
    {
        const string sql = """
        DROP INDEX IF EXISTS ix_doc_it_doc_ix_m__counterparty;
        DROP INDEX IF EXISTS ux_doc_it_doc_ix_m__lines;
        DROP INDEX IF EXISTS ix_cat_it_cat_ix_m__name;

        DROP TABLE IF EXISTS doc_it_doc_ix_m__lines;
        DROP TABLE IF EXISTS doc_it_doc_ix_m;
        DROP TABLE IF EXISTS cat_it_cat_ix_m;
        """;

        await ExecuteAsync(connectionString, sql);
    }

    private static async Task CreateTypedDocumentSchema_WithMismatchedIndexesAsync(string connectionString)
    {
        const string sql = """
        CREATE TABLE IF NOT EXISTS doc_it_doc_ix_m (
            document_id      uuid PRIMARY KEY,
            counterparty_id  uuid NULL,
            total_amount     numeric(18,2) NULL,

            CONSTRAINT fk_doc_it_doc_ix_m__document
                FOREIGN KEY (document_id) REFERENCES documents(id)
                ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS doc_it_doc_ix_m__lines (
            document_id  uuid NOT NULL,
            line_no      int  NOT NULL,
            amount       numeric(18,2) NULL,

            CONSTRAINT pk_doc_it_doc_ix_m__lines
                PRIMARY KEY (document_id, line_no),

            CONSTRAINT fk_doc_it_doc_ix_m__lines__document
                FOREIGN KEY (document_id) REFERENCES documents(id)
                ON DELETE CASCADE
        );

        -- MISMATCH #1: metadata expects (counterparty_id)
        CREATE INDEX IF NOT EXISTS ix_doc_it_doc_ix_m__counterparty
            ON doc_it_doc_ix_m(total_amount);

        -- MISMATCH #2: metadata expects (document_id, line_no)
        CREATE UNIQUE INDEX IF NOT EXISTS ux_doc_it_doc_ix_m__lines
            ON doc_it_doc_ix_m__lines(line_no, document_id);
        """;

        await ExecuteAsync(connectionString, sql);
    }

    private static async Task CreateTypedCatalogSchema_WithMismatchedIndexAsync(string connectionString)
    {
        const string sql = """
        CREATE TABLE IF NOT EXISTS cat_it_cat_ix_m (
            catalog_id uuid PRIMARY KEY REFERENCES catalogs(id) ON DELETE CASCADE,
            name       text NULL
        );

        -- MISMATCH: metadata expects (name)
        CREATE INDEX IF NOT EXISTS ix_cat_it_cat_ix_m__name
            ON cat_it_cat_ix_m(catalog_id);
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
