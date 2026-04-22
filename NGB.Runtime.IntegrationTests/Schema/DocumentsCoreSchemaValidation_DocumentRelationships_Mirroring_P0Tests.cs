using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.Documents.Exceptions;
using NGB.Definitions;
using NGB.Metadata.Base;
using NGB.Metadata.Documents.Hybrid;
using NGB.Persistence.Schema;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Schema;

[Collection(PostgresCollection.Name)]
public sealed class DocumentsCoreSchemaValidation_DocumentRelationships_Mirroring_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ValidateAsync_WhenMirroringInstallerFunctionMissing_ThrowsWithHelpfulMessage()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await DropFunctionAsync(Fixture.ConnectionString, "ngb_install_mirrored_document_relationship_trigger(text,text,text)");

        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<IDocumentsCoreSchemaValidationService>();

        Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
        var ex = await act.Should().ThrowAsync<DocumentSchemaValidationException>();
        ex.Which.Message.Should().Contain("ngb_install_mirrored_document_relationship_trigger");
    }

    [Fact]
    public async Task ValidateAsync_WhenDeclaredMirroredBindingTriggerMissing_ThrowsWithHelpfulMessage()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = CreateMirroredHost();

        await CreateMirroredTableAsync(Fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<IDocumentsCoreSchemaValidationService>();

        Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
        var ex = await act.Should().ThrowAsync<DocumentSchemaValidationException>();
        ex.Which.Message.Should().Contain("Missing mirrored relationship trigger binding");
        ex.Which.Message.Should().Contain("doc_it_declared_mirror.target_document_id");
        ex.Which.Message.Should().Contain("created_from");
    }

    [Fact]
    public async Task ValidateAsync_WhenDeclaredMirroredBindingTriggerInstalled_Succeeds()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = CreateMirroredHost();

        await CreateMirroredTableAsync(Fixture.ConnectionString);
        await InstallMirroredTriggerAsync(Fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<IDocumentsCoreSchemaValidationService>();

        Func<Task> act = () => validator.ValidateAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();
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

    private static async Task DropFunctionAsync(string cs, string signature)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand($"DROP FUNCTION IF EXISTS {signature};", conn);
        await cmd.ExecuteNonQueryAsync();
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
