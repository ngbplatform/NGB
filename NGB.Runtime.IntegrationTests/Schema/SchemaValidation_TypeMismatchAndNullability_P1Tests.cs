using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NGB.Core.Catalogs;
using NGB.Core.Catalogs.Exceptions;
using NGB.Core.Documents;
using NGB.Core.Documents.Exceptions;
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
public sealed class SchemaValidation_TypeMismatchAndNullability_P1Tests(PostgresTestFixture fixture)
{
    private const string DocTypeCode = "it_doc_p1";
    private const string CatalogCode = "it_cat_p1";

    [Fact]
    public async Task DocumentSchemaValidation_WhenRequiredColumnNullable_ThrowsWithSpecificError()
    {
        await fixture.ResetDatabaseAsync();
        await ResetTypedSchemaAsync(fixture.ConnectionString);

        await CreateTypedDocumentSchemaAsync(
            fixture.ConnectionString,
            counterpartyDbType: "uuid",
            counterpartyNullable: true,
            noteDbType: "text");

        using var host = CreateHostWithRegistries(fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<IDocumentSchemaValidationService>();

        await validator.Invoking(v => v.ValidateAllAsync(CancellationToken.None))
            .Should().ThrowAsync<DocumentSchemaValidationException>()
            .WithMessage("*Document schema validation failed*column 'counterparty_id' must be NOT NULL*" );
    }

    [Fact]
    public async Task DocumentSchemaValidation_WhenColumnTypeMismatch_ThrowsWithSpecificError()
    {
        await fixture.ResetDatabaseAsync();
        await ResetTypedSchemaAsync(fixture.ConnectionString);

        await CreateTypedDocumentSchemaAsync(
            fixture.ConnectionString,
            counterpartyDbType: "text",
            counterpartyNullable: false,
            noteDbType: "text");

        using var host = CreateHostWithRegistries(fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<IDocumentSchemaValidationService>();

        await validator.Invoking(v => v.ValidateAllAsync(CancellationToken.None))
            .Should().ThrowAsync<DocumentSchemaValidationException>()
            .WithMessage("*Document schema validation failed*column 'counterparty_id' has type 'text', expected compatible with 'uuid'*" );
    }

    [Fact]
    public async Task DocumentSchemaValidation_WhenStringMaxLengthTooSmall_ReportsLengthError()
    {
        await fixture.ResetDatabaseAsync();
        await ResetTypedSchemaAsync(fixture.ConnectionString);

        await CreateTypedDocumentSchemaAsync(
            fixture.ConnectionString,
            counterpartyDbType: "uuid",
            counterpartyNullable: false,
            noteDbType: "character varying(5)");

        using var host = CreateHostWithRegistries(fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<IDocumentSchemaValidationService>();

        var ex = await validator.Invoking(v => v.ValidateAllAsync(CancellationToken.None))
            .Should().ThrowAsync<DocumentSchemaValidationException>();

        ex.WithMessage("*Document schema validation failed*table 'doc_it_doc_p1' column 'note' max length is 5, expected >= 10*" );
        ex.WithMessage("*Document schema validation failed*column 'note' has type 'character varying', expected compatible with 'text'*" );
    }

    [Fact]
    public async Task CatalogSchemaValidation_WhenRequiredColumnNullable_Throws()
    {
        await fixture.ResetDatabaseAsync();
        await ResetTypedSchemaAsync(fixture.ConnectionString);

        await CreateTypedCatalogSchemaAsync(
            fixture.ConnectionString,
            nameDbType: "text",
            nameNullable: true);

        using var host = CreateHostWithRegistries(fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<ICatalogSchemaValidationService>();

        await validator.Invoking(v => v.ValidateAllAsync(CancellationToken.None))
            .Should().ThrowAsync<CatalogSchemaValidationException>()
            .WithMessage("*Errors:*Catalog 'it_cat_p1': table 'cat_it_cat_p1'.name must be NOT NULL*" );
    }

    [Fact]
    public async Task CatalogSchemaValidation_WhenColumnTypeMismatch_Throws()
    {
        await fixture.ResetDatabaseAsync();
        await ResetTypedSchemaAsync(fixture.ConnectionString);

        await CreateTypedCatalogSchemaAsync(
            fixture.ConnectionString,
            nameDbType: "uuid",
            nameNullable: false);

        using var host = CreateHostWithRegistries(fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<ICatalogSchemaValidationService>();

        await validator.Invoking(v => v.ValidateAllAsync(CancellationToken.None))
            .Should().ThrowAsync<CatalogSchemaValidationException>()
            .WithMessage("*Errors:*Catalog 'it_cat_p1': table 'cat_it_cat_p1'.name has type 'uuid', expected compatible with 'text'*" );
    }

    [Fact]
    public async Task CatalogSchemaValidation_WhenStringMaxLengthTooSmall_ReportsLengthError()
    {
        await fixture.ResetDatabaseAsync();
        await ResetTypedSchemaAsync(fixture.ConnectionString);

        await CreateTypedCatalogSchemaAsync(
            fixture.ConnectionString,
            nameDbType: "character varying(5)",
            nameNullable: false);

        using var host = CreateHostWithRegistries(fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<ICatalogSchemaValidationService>();

        var ex = await validator.Invoking(v => v.ValidateAllAsync(CancellationToken.None))
            .Should().ThrowAsync<CatalogSchemaValidationException>();

        ex.WithMessage("*Errors:*Catalog 'it_cat_p1': table 'cat_it_cat_p1'.name max length is 5, expected >= 10*" );
        ex.WithMessage("*Errors:*Catalog 'it_cat_p1': table 'cat_it_cat_p1'.name has type 'character varying', expected compatible with 'text'*" );
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
                    TableName: "doc_it_doc_p1",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("counterparty_id", ColumnType.Guid, Required: true),
                        new("note", ColumnType.String, Required: false, MaxLength: 10),
                    ],
                    Indexes: [])
            ],
            Presentation: new DocumentPresentationMetadata("Integration Test Doc P1"),
            Version: new DocumentMetadataVersion(1, "integration-tests")
        ));

        return reg;
    }

    private static ICatalogTypeRegistry BuildCatalogRegistry()
    {
        var reg = new CatalogTypeRegistry();

        reg.Register(new CatalogTypeMetadata(
            CatalogCode: CatalogCode,
            DisplayName: "Integration Test Catalog P1",
            Tables:
            [
                new CatalogTableMetadata(
                    TableName: "cat_it_cat_p1",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("catalog_id", ColumnType.Guid, Required: true),
                        new("name", ColumnType.String, Required: true, MaxLength: 10),
                    ],
                    Indexes: [])
            ],
            Presentation: new CatalogPresentationMetadata("cat_it_cat_p1", "name"),
            Version: new CatalogMetadataVersion(1, "integration-tests")
        ));

        return reg;
    }

    private static async Task ResetTypedSchemaAsync(string connectionString)
    {
        const string sql = """
        DROP TABLE IF EXISTS doc_it_doc_p1;
        DROP TABLE IF EXISTS cat_it_cat_p1;
        """;

        await ExecuteAsync(connectionString, sql);
    }

    private static async Task CreateTypedDocumentSchemaAsync(
        string connectionString,
        string counterpartyDbType,
        bool counterpartyNullable,
        string noteDbType)
    {
        var sql = $"""
        CREATE TABLE IF NOT EXISTS doc_it_doc_p1 (
            document_id     uuid PRIMARY KEY,
            counterparty_id {counterpartyDbType} {(counterpartyNullable ? "NULL" : "NOT NULL")},
            note            {noteDbType} NULL,

            CONSTRAINT fk_doc_it_doc_p1__document
                FOREIGN KEY (document_id) REFERENCES documents(id)
                ON DELETE CASCADE
        );
        """;

        await ExecuteAsync(connectionString, sql);
    }

    private static async Task CreateTypedCatalogSchemaAsync(
        string connectionString,
        string nameDbType,
        bool nameNullable)
    {
        var sql = $"""
        CREATE TABLE IF NOT EXISTS cat_it_cat_p1 (
            catalog_id uuid PRIMARY KEY REFERENCES catalogs(id) ON DELETE CASCADE,
            name       {nameDbType} {(nameNullable ? "NULL" : "NOT NULL")}
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
