using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Definitions;
using NGB.Metadata.Base;
using NGB.Metadata.Documents.Hybrid;
using NGB.Persistence.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(PostgresCollection.Name)]
public sealed class DocumentDisplayReader_MixedBatch_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string TypedTypeA = "it_doc_display_a";
    private const string TypedTypeB = "it_doc_display_b";
    private const string GenericType = "it_doc_display_generic";
    private const string TypedHeadTableA = "doc_it_doc_display_a";
    private const string TypedHeadTableB = "doc_it_doc_display_b";

    [Fact]
    public async Task ResolveRefsAsync_PrefersTypedDisplay_AndFallsBackToGenericOrShortGuid_InSingleBatch()
    {
        using var host = CreateHost(Fixture.ConnectionString);

        await EnsureTypedHeadTableAsync(Fixture.ConnectionString, TypedHeadTableA);
        await EnsureTypedHeadTableAsync(Fixture.ConnectionString, TypedHeadTableB);

        var typedAId = Guid.CreateVersion7();
        var typedBId = Guid.CreateVersion7();
        var genericId = Guid.CreateVersion7();
        var missingId = Guid.CreateVersion7();

        await SeedDocumentAsync(Fixture.ConnectionString, typedAId, TypedTypeA, "DOC-A");
        await SeedDocumentAsync(Fixture.ConnectionString, typedBId, TypedTypeB, "DOC-B");
        await SeedDocumentAsync(Fixture.ConnectionString, genericId, GenericType, "DOC-C");
        await SeedTypedHeadAsync(Fixture.ConnectionString, TypedHeadTableA, typedAId, "Lease A-101");
        await SeedTypedHeadAsync(Fixture.ConnectionString, TypedHeadTableB, typedBId, "Invoice B-202");

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IDocumentDisplayReader>();

        var result = await reader.ResolveRefsAsync([typedAId, typedBId, genericId, missingId], CancellationToken.None);

        result[typedAId].Should().Be(new DocumentDisplayRef(typedAId, TypedTypeA, "Lease A-101"));
        result[typedBId].Should().Be(new DocumentDisplayRef(typedBId, TypedTypeB, "Invoice B-202"));
        result[genericId].Should().Be(new DocumentDisplayRef(genericId, GenericType, "Generic Doc DOC-C"));
        result[missingId].TypeCode.Should().BeEmpty();
        result[missingId].Display.Should().Be(missingId.ToString("N")[..8]);
    }

    private static IHost CreateHost(string connectionString)
    {
        return IntegrationHostFactory.Create(connectionString, services =>
        {
            services.AddSingleton<IDefinitionsContributor, TestDocumentsContributor>();
        });
    }

    private static async Task EnsureTypedHeadTableAsync(string connectionString, string tableName)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(CancellationToken.None);

        var sql = $"""
                  CREATE TABLE IF NOT EXISTS {tableName} (
                      document_id UUID PRIMARY KEY REFERENCES documents(id) ON DELETE CASCADE,
                      display TEXT NULL
                  );
                  """;

        await conn.ExecuteAsync(sql);
    }

    private static async Task SeedTypedHeadAsync(string connectionString, string tableName, Guid id, string display)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(CancellationToken.None);

        var sql = $"""
                  INSERT INTO {tableName}(document_id, display)
                  VALUES (@id, @display)
                  ON CONFLICT (document_id) DO UPDATE SET display = EXCLUDED.display;
                  """;

        await conn.ExecuteAsync(sql, new { id, display });
    }

    private static async Task SeedDocumentAsync(string connectionString, Guid id, string typeCode, string number)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(CancellationToken.None);

        var nowUtc = DateTime.UtcNow;

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
                               @nowUtc,
                               1,
                               NULL,
                               NULL,
                               @nowUtc,
                               @nowUtc
                           )
                           ON CONFLICT (id) DO UPDATE SET
                               type_code = EXCLUDED.type_code,
                               number = EXCLUDED.number,
                               updated_at_utc = EXCLUDED.updated_at_utc;
                           """;

        await conn.ExecuteAsync(sql, new { id, typeCode, number, nowUtc });
    }

    private sealed class TestDocumentsContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument(TypedTypeA, d => d.Metadata(new DocumentTypeMetadata(
                TypeCode: TypedTypeA,
                Tables:
                [
                    new DocumentTableMetadata(
                        TableName: TypedHeadTableA,
                        Kind: TableKind.Head,
                        Columns:
                        [
                            new DocumentColumnMetadata("display", ColumnType.String)
                        ])
                ],
                Presentation: new DocumentPresentationMetadata("Lease Doc"),
                Version: new DocumentMetadataVersion(1, "tests"))));

            builder.AddDocument(TypedTypeB, d => d.Metadata(new DocumentTypeMetadata(
                TypeCode: TypedTypeB,
                Tables:
                [
                    new DocumentTableMetadata(
                        TableName: TypedHeadTableB,
                        Kind: TableKind.Head,
                        Columns:
                        [
                            new DocumentColumnMetadata("display", ColumnType.String)
                        ])
                ],
                Presentation: new DocumentPresentationMetadata("Invoice Doc"),
                Version: new DocumentMetadataVersion(1, "tests"))));

            builder.AddDocument(GenericType, d => d.Metadata(new DocumentTypeMetadata(
                TypeCode: GenericType,
                Tables: Array.Empty<DocumentTableMetadata>(),
                Presentation: new DocumentPresentationMetadata("Generic Doc"),
                Version: new DocumentMetadataVersion(1, "tests"))));
        }
    }
}
