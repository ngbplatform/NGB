using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NGB.Core.Catalogs.Exceptions;
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
public sealed class SchemaValidation_ColumnTypeMatrix_P1Tests(PostgresTestFixture fixture)
{
    private const string DocTypeCode = "it_doc_matrix_p1";
    private const string CatalogCode = "it_cat_matrix_p1";

    [Fact]
    public async Task DocumentSchemaValidation_WithAllSupportedColumnTypes_Passes()
    {
        await fixture.ResetDatabaseAsync();
        await ResetSchemaAsync(fixture.ConnectionString);
        await CreateDocumentSchemaAsync(fixture.ConnectionString, overrideColumnName: null, overrideDbType: null);

        using var host = CreateHostWithRegistries(fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<IDocumentSchemaValidationService>();

        await validator.Invoking(v => v.ValidateAllAsync(CancellationToken.None))
            .Should().NotThrowAsync();
    }

    [Theory]
    // String
    [InlineData("txt", "character varying(10)", "text")]
    // Guid
    [InlineData("guid2", "text", "uuid")]
    // Int32
    [InlineData("i32", "bigint", "integer")]
    // Int64
    [InlineData("i64", "integer", "bigint")]
    // Decimal
    [InlineData("dec", "integer", "numeric")]
    // Boolean
    [InlineData("flag", "integer", "boolean")]
    // DateTimeUtc
    [InlineData("dt", "timestamp without time zone", "timestamp with time zone")]
    // Date
    [InlineData("d", "timestamp with time zone", "date")]
    // Json
    [InlineData("j", "json", "jsonb")]
    public async Task DocumentSchemaValidation_WhenColumnTypeMismatch_ReportsExpectedError(
        string column,
        string actualDbType,
        string expectedDbType)
    {
        await fixture.ResetDatabaseAsync();
        await ResetSchemaAsync(fixture.ConnectionString);
        await CreateDocumentSchemaAsync(fixture.ConnectionString, overrideColumnName: column, overrideDbType: actualDbType);

        using var host = CreateHostWithRegistries(fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<IDocumentSchemaValidationService>();

        await validator.Invoking(v => v.ValidateAllAsync(CancellationToken.None))
            .Should().ThrowAsync<DocumentSchemaValidationException>()
            .WithMessage($"*Document type '{DocTypeCode}': table 'doc_{DocTypeCode}' column '{column}' has type '*', expected compatible with '{expectedDbType}'*");
    }

    [Fact]
    public async Task CatalogSchemaValidation_WithAllSupportedColumnTypes_Passes()
    {
        await fixture.ResetDatabaseAsync();
        await ResetSchemaAsync(fixture.ConnectionString);
        await CreateCatalogSchemaAsync(fixture.ConnectionString, overrideColumnName: null, overrideDbType: null);

        using var host = CreateHostWithRegistries(fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<ICatalogSchemaValidationService>();

        await validator.Invoking(v => v.ValidateAllAsync(CancellationToken.None))
            .Should().NotThrowAsync();
    }

    [Theory]
    // String
    [InlineData("name", "character varying(10)", "text")]
    // Int32
    [InlineData("i32", "bigint", "integer")]
    // Int64
    [InlineData("i64", "integer", "bigint")]
    // Decimal
    [InlineData("dec", "integer", "numeric")]
    // Boolean
    [InlineData("flag", "integer", "boolean")]
    // DateTimeUtc
    [InlineData("dt", "timestamp without time zone", "timestamp with time zone")]
    // Date
    [InlineData("d", "timestamp with time zone", "date")]
    // Json
    [InlineData("j", "json", "jsonb")]
    public async Task CatalogSchemaValidation_WhenColumnTypeMismatch_ReportsExpectedError(
        string column,
        string actualDbType,
        string expectedDbType)
    {
        await fixture.ResetDatabaseAsync();
        await ResetSchemaAsync(fixture.ConnectionString);
        await CreateCatalogSchemaAsync(fixture.ConnectionString, overrideColumnName: column, overrideDbType: actualDbType);

        using var host = CreateHostWithRegistries(fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<ICatalogSchemaValidationService>();

        await validator.Invoking(v => v.ValidateAllAsync(CancellationToken.None))
            .Should().ThrowAsync<CatalogSchemaValidationException>()
            .WithMessage($"*Errors:*Catalog '{CatalogCode}': table 'cat_{CatalogCode}'.{column} has type '*', expected compatible with '{expectedDbType}'*");
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
                    TableName: $"doc_{DocTypeCode}",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("txt", ColumnType.String),
                        new("guid2", ColumnType.Guid),
                        new("i32", ColumnType.Int32),
                        new("i64", ColumnType.Int64),
                        new("dec", ColumnType.Decimal),
                        new("flag", ColumnType.Boolean),
                        new("dt", ColumnType.DateTimeUtc),
                        new("d", ColumnType.Date),
                        new("j", ColumnType.Json),
                    ],
                    Indexes: [])
            ],
            Presentation: new DocumentPresentationMetadata("Integration Test Doc Matrix"),
            Version: new DocumentMetadataVersion(1, "integration-tests")
        ));

        return reg;
    }

    private static ICatalogTypeRegistry BuildCatalogRegistry()
    {
        var reg = new CatalogTypeRegistry();

        reg.Register(new CatalogTypeMetadata(
            CatalogCode: CatalogCode,
            DisplayName: "Integration Test Catalog Matrix",
            Tables:
            [
                new CatalogTableMetadata(
                    TableName: $"cat_{CatalogCode}",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("catalog_id", ColumnType.Guid, Required: true),
                        new("name", ColumnType.String),
                        new("i32", ColumnType.Int32),
                        new("i64", ColumnType.Int64),
                        new("dec", ColumnType.Decimal),
                        new("flag", ColumnType.Boolean),
                        new("dt", ColumnType.DateTimeUtc),
                        new("d", ColumnType.Date),
                        new("j", ColumnType.Json),
                    ],
                    Indexes: [])
            ],
            Presentation: new CatalogPresentationMetadata($"cat_{CatalogCode}", "name"),
            Version: new CatalogMetadataVersion(1, "integration-tests")
        ));

        return reg;
    }

    private static async Task ResetSchemaAsync(string cs)
    {
        var sql = $"""
        DROP TABLE IF EXISTS doc_{DocTypeCode};
        DROP TABLE IF EXISTS cat_{CatalogCode};
        """;

        await ExecuteAsync(cs, sql);
    }

    private static async Task CreateDocumentSchemaAsync(string cs, string? overrideColumnName, string? overrideDbType)
    {
        string DbType(string column, string defaultType)
            => string.Equals(column, overrideColumnName, StringComparison.OrdinalIgnoreCase) && overrideDbType is not null
                ? overrideDbType
                : defaultType;

        var sql = $"""
        CREATE TABLE IF NOT EXISTS doc_{DocTypeCode} (
            document_id uuid PRIMARY KEY,
            txt   {DbType("txt", "text")} NULL,
            guid2 {DbType("guid2", "uuid")} NULL,
            i32   {DbType("i32", "integer")} NULL,
            i64   {DbType("i64", "bigint")} NULL,
            dec   {DbType("dec", "numeric(18,2)")} NULL,
            flag  {DbType("flag", "boolean")} NULL,
            dt    {DbType("dt", "timestamp with time zone")} NULL,
            d     {DbType("d", "date")} NULL,
            j     {DbType("j", "jsonb")} NULL,

            CONSTRAINT fk_doc_{DocTypeCode}__document
                FOREIGN KEY (document_id) REFERENCES documents(id)
                ON DELETE CASCADE
        );
        """;

        await ExecuteAsync(cs, sql);
    }

    private static async Task CreateCatalogSchemaAsync(string cs, string? overrideColumnName, string? overrideDbType)
    {
        string DbType(string column, string defaultType)
            => string.Equals(column, overrideColumnName, StringComparison.OrdinalIgnoreCase) && overrideDbType is not null
                ? overrideDbType
                : defaultType;

        var sql = $"""
        CREATE TABLE IF NOT EXISTS cat_{CatalogCode} (
            catalog_id uuid PRIMARY KEY REFERENCES catalogs(id) ON DELETE CASCADE,
            name {DbType("name", "text")} NULL,
            i32  {DbType("i32", "integer")} NULL,
            i64  {DbType("i64", "bigint")} NULL,
            dec  {DbType("dec", "numeric(18,2)")} NULL,
            flag {DbType("flag", "boolean")} NULL,
            dt   {DbType("dt", "timestamp with time zone")} NULL,
            d    {DbType("d", "date")} NULL,
            j    {DbType("j", "jsonb")} NULL
        );
        """;

        await ExecuteAsync(cs, sql);
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }
}
