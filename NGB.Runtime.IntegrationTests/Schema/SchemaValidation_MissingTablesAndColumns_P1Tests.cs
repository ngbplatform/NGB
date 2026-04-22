using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NGB.Core.Catalogs;
using NGB.Core.Catalogs.Exceptions;
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
public sealed class SchemaValidation_MissingTablesAndColumns_P1Tests(PostgresTestFixture fixture)
{
    private const string DocTypeCode = "it_doc_missing_p1";
    private const string CatalogCode = "it_cat_missing_p1";

    [Fact]
    public async Task DocumentSchemaValidation_WhenTableMissing_Throws()
    {
        await fixture.ResetDatabaseAsync();
        await ResetSchemaAsync(fixture.ConnectionString);

        using var host = CreateHostWithRegistries(fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<IDocumentSchemaValidationService>();

        await validator.Invoking(v => v.ValidateAllAsync(CancellationToken.None))
            .Should().ThrowAsync<DocumentSchemaValidationException>()
            .WithMessage($"*Document type '{DocTypeCode}': missing table 'doc_{DocTypeCode}'*");
    }

    [Fact]
    public async Task DocumentSchemaValidation_WhenDocumentIdColumnMissing_Throws()
    {
        await fixture.ResetDatabaseAsync();
        await ResetSchemaAsync(fixture.ConnectionString);
        await ExecuteAsync(fixture.ConnectionString, $"CREATE TABLE doc_{DocTypeCode} (x integer NULL);");

        using var host = CreateHostWithRegistries(fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<IDocumentSchemaValidationService>();

        await validator.Invoking(v => v.ValidateAllAsync(CancellationToken.None))
            .Should().ThrowAsync<DocumentSchemaValidationException>()
            .WithMessage($"*Document type '{DocTypeCode}': table 'doc_{DocTypeCode}' must have 'document_id' column referencing documents(id).*" );
    }

    [Fact]
    public async Task DocumentSchemaValidation_WhenDocumentIdFkMissing_Throws()
    {
        await fixture.ResetDatabaseAsync();
        await ResetSchemaAsync(fixture.ConnectionString);

        var sql = $"""
        CREATE TABLE doc_{DocTypeCode} (
            document_id uuid PRIMARY KEY,
            amount numeric NULL
        );
        """;

        await ExecuteAsync(fixture.ConnectionString, sql);

        using var host = CreateHostWithRegistries(fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<IDocumentSchemaValidationService>();

        await validator.Invoking(v => v.ValidateAllAsync(CancellationToken.None))
            .Should().ThrowAsync<DocumentSchemaValidationException>()
            .WithMessage($"*Document type '{DocTypeCode}': table 'doc_{DocTypeCode}' must have FK on 'document_id' -> documents(id).*" );
    }

    [Fact]
    public async Task DocumentSchemaValidation_WhenMetadataColumnMissing_Throws()
    {
        await fixture.ResetDatabaseAsync();
        await ResetSchemaAsync(fixture.ConnectionString);

        var sql = $"""
        CREATE TABLE doc_{DocTypeCode} (
            document_id uuid PRIMARY KEY REFERENCES documents(id) ON DELETE CASCADE
        );
        """;

        await ExecuteAsync(fixture.ConnectionString, sql);

        using var host = CreateHostWithRegistries(fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<IDocumentSchemaValidationService>();

        await validator.Invoking(v => v.ValidateAllAsync(CancellationToken.None))
            .Should().ThrowAsync<DocumentSchemaValidationException>()
            .WithMessage($"*Document type '{DocTypeCode}': table 'doc_{DocTypeCode}' missing column 'amount'.*" );
    }

    [Fact]
    public async Task CatalogSchemaValidation_WhenTableMissing_Throws()
    {
        await fixture.ResetDatabaseAsync();
        await ResetSchemaAsync(fixture.ConnectionString);

        using var host = CreateHostWithRegistries(fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<ICatalogSchemaValidationService>();

        await validator.Invoking(v => v.ValidateAllAsync(CancellationToken.None))
            .Should().ThrowAsync<CatalogSchemaValidationException>()
            .WithMessage($"*Errors:*Catalog '{CatalogCode}': table 'cat_{CatalogCode}' does not exist.*" );
    }

    [Fact]
    public async Task CatalogSchemaValidation_WhenCatalogIdColumnMissing_Throws()
    {
        await fixture.ResetDatabaseAsync();
        await ResetSchemaAsync(fixture.ConnectionString);

        await ExecuteAsync(fixture.ConnectionString, $"CREATE TABLE cat_{CatalogCode} (name text NULL);");

        using var host = CreateHostWithRegistries(fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<ICatalogSchemaValidationService>();

        await validator.Invoking(v => v.ValidateAllAsync(CancellationToken.None))
            .Should().ThrowAsync<CatalogSchemaValidationException>()
            .WithMessage($"*Errors:*Catalog '{CatalogCode}': table 'cat_{CatalogCode}' must have column 'catalog_id'.*" +
                         $"*Catalog '{CatalogCode}': table 'cat_{CatalogCode}' must have FK catalog_id -> catalogs(id).*" );
    }

    [Fact]
    public async Task CatalogSchemaValidation_WhenCatalogIdFkMissing_Throws()
    {
        await fixture.ResetDatabaseAsync();
        await ResetSchemaAsync(fixture.ConnectionString);

        var sql = $"""
        CREATE TABLE cat_{CatalogCode} (
            catalog_id uuid PRIMARY KEY,
            name text NULL
        );
        """;

        await ExecuteAsync(fixture.ConnectionString, sql);

        using var host = CreateHostWithRegistries(fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<ICatalogSchemaValidationService>();

        await validator.Invoking(v => v.ValidateAllAsync(CancellationToken.None))
            .Should().ThrowAsync<CatalogSchemaValidationException>()
            .WithMessage($"*Errors:*Catalog '{CatalogCode}': table 'cat_{CatalogCode}' must have FK catalog_id -> catalogs(id).*" );
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
                        new("amount", ColumnType.Decimal, Required: true)
                    ],
                    Indexes: [])
            ],
            Presentation: new DocumentPresentationMetadata("Integration Test Doc Missing"),
            Version: new DocumentMetadataVersion(1, "integration-tests")
        ));

        return reg;
    }

    private static ICatalogTypeRegistry BuildCatalogRegistry()
    {
        var reg = new CatalogTypeRegistry();

        reg.Register(new CatalogTypeMetadata(
            CatalogCode: CatalogCode,
            DisplayName: "Integration Test Catalog Missing",
            Tables:
            [
                new CatalogTableMetadata(
                    TableName: $"cat_{CatalogCode}",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("catalog_id", ColumnType.Guid, Required: true),
                        new("name", ColumnType.String)
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

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }
}
