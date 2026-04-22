using Dapper;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Definitions;
using NGB.Metadata.Base;
using NGB.Metadata.Documents.Hybrid;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

public sealed class DocumentService_Parts_GetById_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture), IClassFixture<PostgresTestFixture>
{
    private const string TypeCode = "it_doc_parts";

    [Fact]
    public async Task GetByIdAsync_WhenDocumentHasPartRows_ReturnsPayloadParts()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            configureTestServices: services =>
            {
                services.AddSingleton<IDefinitionsContributor, ItDocPartsContributor>();
            });

        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
        var svc = scope.ServiceProvider.GetRequiredService<IDocumentService>();

        await uow.EnsureConnectionOpenAsync();

        // Use TEMP tables to keep the integration test schema isolated from Respawn.
        await uow.Connection.ExecuteAsync(
            """
            CREATE TEMP TABLE it_doc_parts (
                document_id uuid PRIMARY KEY,
                display     text NOT NULL,
                foo         int  NOT NULL
            );

            CREATE TEMP TABLE it_doc_parts__lines (
                document_id uuid NOT NULL,
                ordinal     int  NOT NULL,
                note        text NULL
            );
            """);

        var id = await drafts.CreateDraftAsync(
            typeCode: TypeCode,
            number: null,
            dateUtc: DateTime.UtcNow,
            manageTransaction: true,
            ct: CancellationToken.None);

        await uow.Connection.ExecuteAsync(
            "INSERT INTO it_doc_parts(document_id, display, foo) VALUES (@id, @display, @foo);",
            new { id, display = "Lease A", foo = 123 });

        await uow.Connection.ExecuteAsync(
            """
            INSERT INTO it_doc_parts__lines(document_id, ordinal, note)
            VALUES (@id, 1, 'L1'), (@id, 2, 'L2');
            """,
            new { id });

        var dto = await svc.GetByIdAsync(TypeCode, id, CancellationToken.None);

        dto.Id.Should().Be(id);
        dto.Display.Should().Be("Lease A");
        dto.Payload.Fields.Should().NotBeNull();
        dto.Payload.Fields!.Should().ContainKey("foo");

        dto.Payload.Parts.Should().NotBeNull();
        dto.Payload.Parts!.Should().ContainKey("lines");

        var lines = dto.Payload.Parts!["lines"].Rows;
        lines.Should().HaveCount(2);

        lines[0]["ordinal"].GetInt32().Should().Be(1);
        lines[0]["note"].GetString().Should().Be("L1");
        lines[1]["ordinal"].GetInt32().Should().Be(2);
        lines[1]["note"].GetString().Should().Be("L2");
    }

    [Fact]
    public async Task CreateAndUpdateDraftAsync_WithParts_PersistsAndReplacesPartRows()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            configureTestServices: services =>
            {
                services.AddSingleton<IDefinitionsContributor, ItDocPartsContributor>();
            });

        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var svc = scope.ServiceProvider.GetRequiredService<IDocumentService>();

        await uow.EnsureConnectionOpenAsync();

        // Use TEMP tables to keep the integration test schema isolated from Respawn.
        await uow.Connection.ExecuteAsync(
            """
            CREATE TEMP TABLE it_doc_parts (
                document_id uuid PRIMARY KEY,
                display     text NOT NULL,
                foo         int  NOT NULL
            );

            CREATE TEMP TABLE it_doc_parts__lines (
                document_id uuid NOT NULL,
                ordinal     int  NOT NULL,
                note        text NULL,
                CONSTRAINT ux_it_doc_parts__lines UNIQUE (document_id, ordinal)
            );
            """);

        // Create with parts.
        var created = await svc.CreateDraftAsync(
            TypeCode,
            new RecordPayload(
                Fields: new Dictionary<string, JsonElement>
                {
                    ["display"] = JsonSerializer.SerializeToElement("Doc A"),
                    ["foo"] = JsonSerializer.SerializeToElement(10),
                },
                Parts: new Dictionary<string, RecordPartPayload>
                {
                    ["lines"] = new RecordPartPayload(
                    [
                        new Dictionary<string, JsonElement>
                        {
                            ["ordinal"] = JsonSerializer.SerializeToElement(1),
                            ["note"] = JsonSerializer.SerializeToElement("L1"),
                        },
                        new Dictionary<string, JsonElement>
                        {
                            ["ordinal"] = JsonSerializer.SerializeToElement(2),
                            ["note"] = JsonSerializer.SerializeToElement("L2"),
                        }
                    ])
                }),
            CancellationToken.None);

        created.Payload.Parts.Should().NotBeNull();
        created.Payload.Parts!.Should().ContainKey("lines");

        var dbRows = (await uow.Connection.QueryAsync<(int Ordinal, string? Note)>(
            "SELECT ordinal, note FROM it_doc_parts__lines WHERE document_id = @id ORDER BY ordinal;",
            new { id = created.Id })).ToList();

        dbRows.Should().HaveCount(2);
        dbRows[0].Ordinal.Should().Be(1);
        dbRows[0].Note.Should().Be("L1");
        dbRows[1].Ordinal.Should().Be(2);
        dbRows[1].Note.Should().Be("L2");

        // Update: replace parts (and keep head intact with partial update).
        var updated = await svc.UpdateDraftAsync(
            TypeCode,
            created.Id,
            new RecordPayload(
                Fields: null,
                Parts: new Dictionary<string, RecordPartPayload>
                {
                    ["lines"] = new RecordPartPayload(
                    [
                        new Dictionary<string, JsonElement>
                        {
                            ["ordinal"] = JsonSerializer.SerializeToElement(1),
                            ["note"] = JsonSerializer.SerializeToElement("R1"),
                        }
                    ])
                }),
            CancellationToken.None);

        var dbRows2 = (await uow.Connection.QueryAsync<(int Ordinal, string? Note)>(
            "SELECT ordinal, note FROM it_doc_parts__lines WHERE document_id = @id ORDER BY ordinal;",
            new { id = updated.Id })).ToList();

        dbRows2.Should().HaveCount(1);
        dbRows2[0].Ordinal.Should().Be(1);
        dbRows2[0].Note.Should().Be("R1");

        // Update: empty rows list clears the part table for the document.
        await svc.UpdateDraftAsync(
            TypeCode,
            created.Id,
            new RecordPayload(
                Parts: new Dictionary<string, RecordPartPayload>
                {
                    ["lines"] = new RecordPartPayload([])
                }),
            CancellationToken.None);

        var count = await uow.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM it_doc_parts__lines WHERE document_id = @id;",
            new { id = created.Id });

        count.Should().Be(0);
    }

    private sealed class ItDocPartsContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument(TypeCode, d => d.Metadata(new DocumentTypeMetadata(
                TypeCode: TypeCode,
                Tables:
                [
                    new DocumentTableMetadata(
                        TableName: "it_doc_parts",
                        Kind: TableKind.Head,
                        Columns:
                        [
                            new("document_id", ColumnType.Guid, Required: true),
                            new("display", ColumnType.String, Required: true),
                            new("foo", ColumnType.Int32, Required: true),
                        ]),
                    new DocumentTableMetadata(
                        TableName: "it_doc_parts__lines",
                        Kind: TableKind.Part,
                        PartCode: "lines",
                        Columns:
                        [
                            new("document_id", ColumnType.Guid, Required: true),
                            new("ordinal", ColumnType.Int32, Required: true),
                            new("note", ColumnType.String),
                        ]),
                ],
                Presentation: new DocumentPresentationMetadata("IT Doc Parts"),
                Version: new DocumentMetadataVersion(1, "it-tests"))));
        }
    }
}
