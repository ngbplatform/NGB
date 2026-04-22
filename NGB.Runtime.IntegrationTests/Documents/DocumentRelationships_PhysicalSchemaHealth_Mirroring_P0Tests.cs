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
public sealed class DocumentRelationships_PhysicalSchemaHealth_Mirroring_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task GetAsync_WhenDeclaredMirroredBindingIsInstalled_ReportsNoMissingBindings()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = CreateMirroredHost();

        await CreateMirroredTableAsync(Fixture.ConnectionString);
        await InstallMirroredTriggerAsync(Fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipsPhysicalSchemaHealthReader>();

        var health = await reader.GetAsync(CancellationToken.None);

        health.MissingMirroredTriggerBindings.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAsync_WhenDeclaredMirroredBindingTriggerIsMissing_ReportsHelpfulDescriptor()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = CreateMirroredHost();

        await CreateMirroredTableAsync(Fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipsPhysicalSchemaHealthReader>();

        var health = await reader.GetAsync(CancellationToken.None);

        health.MissingMirroredTriggerBindings.Should().ContainSingle();
        health.MissingMirroredTriggerBindings[0].Should().Contain("it_doc_mirror_declared");
        health.MissingMirroredTriggerBindings[0].Should().Contain("doc_it_declared_mirror.target_document_id");
        health.MissingMirroredTriggerBindings[0].Should().Contain("created_from");
    }

    private IHost CreateMirroredHost()
        => IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services => services.AddSingleton<IDefinitionsContributor, MirroredDocumentContributor>());

    private static async Task CreateMirroredTableAsync(string cs)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        await conn.ExecuteAsync(
            "drop table if exists doc_it_declared_mirror cascade; " +
            "create table doc_it_declared_mirror (document_id uuid primary key references documents(id) on delete cascade, target_document_id uuid null);");
    }

    private static async Task InstallMirroredTriggerAsync(string cs)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        await conn.ExecuteAsync(
            "select ngb_install_mirrored_document_relationship_trigger(@tableName, @columnName, @relationshipCode);",
            new { tableName = "doc_it_declared_mirror", columnName = "target_document_id", relationshipCode = "created_from" });
    }

    private sealed class MirroredDocumentContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument("it_doc_mirror_declared", b => b.Metadata(new DocumentTypeMetadata(
                TypeCode: "it_doc_mirror_declared",
                Tables:
                [
                    new DocumentTableMetadata(
                        TableName: "doc_it_declared_mirror",
                        Kind: TableKind.Head,
                        Columns:
                        [
                            new DocumentColumnMetadata(
                                ColumnName: "target_document_id",
                                Type: ColumnType.Guid,
                                Lookup: new DocumentLookupSourceMetadata(["it_doc_target_declared"]),
                                MirroredRelationship: new MirroredDocumentRelationshipMetadata("created_from"))
                        ])
                ],
                Presentation: new DocumentPresentationMetadata("IT Declared Mirror"),
                Version: new DocumentMetadataVersion(1, "it-tests"))));

            builder.AddDocument("it_doc_target_declared", b => b.Metadata(new DocumentTypeMetadata(
                TypeCode: "it_doc_target_declared",
                Tables: Array.Empty<DocumentTableMetadata>(),
                Presentation: new DocumentPresentationMetadata("IT Target"),
                Version: new DocumentMetadataVersion(1, "it-tests"))));
        }
    }
}
