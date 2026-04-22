using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Core.Documents.Exceptions;
using NGB.Definitions;
using NGB.Metadata.Base;
using NGB.Metadata.Documents.Hybrid;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Documents.Derivations;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(PostgresCollection.Name)]
public sealed class DocumentService_DeriveAsync_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string SourceTypeCode = "it_docsvc_alpha";
    private const string TargetTypeCode = "it_docsvc_beta";

    [Fact]
    public async Task DeriveAsync_MatchingDerivation_CreatesTargetDraft_AndWritesRelationship()
    {
        using var host = CreateHost(registerDuplicateDerivation: false);
        await EnsureTablesAsync(host);
        await using var scope = host.Services.CreateAsyncScope();

        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

        var source = await documents.CreateDraftAsync(
            SourceTypeCode,
            Payload(new { display = "Source Alpha" }),
            CancellationToken.None);

        var derived = await documents.DeriveAsync(
            TargetTypeCode,
            source.Id,
            relationshipType: "created_from",
            initialPayload: null,
            CancellationToken.None);

        derived.Display.Should().Be("Derived draft");
        derived.Payload.Fields.Should().NotBeNull();
        derived.Payload.Fields!["memo"].GetString().Should().Be("handler memo");
        derived.Payload.Parts.Should().NotBeNull();
        derived.Payload.Parts!["lines"].Rows.Should().ContainSingle();
        derived.Payload.Parts!["lines"].Rows[0]["note"].GetString().Should().Be("handler line");

        var flow = await documents.GetRelationshipGraphAsync(
            TargetTypeCode,
            derived.Id,
            depth: 3,
            maxNodes: 20,
            CancellationToken.None);

        flow.Edges.Should().ContainSingle(edge =>
            string.Equals(edge.RelationshipType, "created_from", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DeriveAsync_WhenInitialPayloadProvided_OverlaysHandlerDraft()
    {
        using var host = CreateHost(registerDuplicateDerivation: false);
        await EnsureTablesAsync(host);
        await using var scope = host.Services.CreateAsyncScope();

        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

        var source = await documents.CreateDraftAsync(
            SourceTypeCode,
            Payload(new { display = "Source Alpha" }),
            CancellationToken.None);

        var derived = await documents.DeriveAsync(
            TargetTypeCode,
            source.Id,
            relationshipType: "created_from",
            initialPayload: new RecordPayload(
                Fields: new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
                {
                    ["display"] = JsonSerializer.SerializeToElement("Payload draft"),
                    ["memo"] = JsonSerializer.SerializeToElement("payload memo")
                },
                Parts: new Dictionary<string, RecordPartPayload>(StringComparer.OrdinalIgnoreCase)
                {
                    ["lines"] = new RecordPartPayload(
                    [
                        new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["ordinal"] = JsonSerializer.SerializeToElement(1),
                            ["note"] = JsonSerializer.SerializeToElement("payload line")
                        }
                    ])
                }),
            CancellationToken.None);

        derived.Display.Should().Be("Payload draft");
        derived.Payload.Fields.Should().NotBeNull();
        derived.Payload.Fields!["memo"].GetString().Should().Be("payload memo");
        derived.Payload.Parts.Should().NotBeNull();
        derived.Payload.Parts!["lines"].Rows.Should().ContainSingle();
        derived.Payload.Parts!["lines"].Rows[0]["note"].GetString().Should().Be("payload line");
    }

    [Fact]
    public async Task DeriveAsync_WhenNoMatchingRelationship_ThrowsDocumentDerivationNotFound()
    {
        using var host = CreateHost(registerDuplicateDerivation: false);
        await EnsureTablesAsync(host);
        await using var scope = host.Services.CreateAsyncScope();

        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

        var source = await documents.CreateDraftAsync(
            SourceTypeCode,
            Payload(new { display = "Source Alpha" }),
            CancellationToken.None);

        var act = () => documents.DeriveAsync(
            TargetTypeCode,
            source.Id,
            relationshipType: "based_on",
            initialPayload: null,
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DocumentDerivationNotFoundException>();
        ex.Which.AssertNgbError(DocumentDerivationNotFoundException.Code, "derivationCode");
    }

    [Fact]
    public async Task DeriveAsync_WhenSourceDocumentMissing_ThrowsDocumentNotFound()
    {
        using var host = CreateHost(registerDuplicateDerivation: false);
        await EnsureTablesAsync(host);
        await using var scope = host.Services.CreateAsyncScope();

        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();
        var missingId = Guid.CreateVersion7();

        var act = () => documents.DeriveAsync(
            TargetTypeCode,
            missingId,
            relationshipType: "created_from",
            initialPayload: null,
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DocumentNotFoundException>();
        ex.Which.AssertNgbError(DocumentNotFoundException.Code, "documentId");
    }

    [Fact]
    public async Task DeriveAsync_WhenMultipleMatchesExist_ThrowsConfigurationViolation()
    {
        using var host = CreateHost(registerDuplicateDerivation: true);
        await EnsureTablesAsync(host);
        await using var scope = host.Services.CreateAsyncScope();

        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

        var source = await documents.CreateDraftAsync(
            SourceTypeCode,
            Payload(new { display = "Source Alpha" }),
            CancellationToken.None);

        var act = () => documents.DeriveAsync(
            TargetTypeCode,
            source.Id,
            relationshipType: "created_from",
            initialPayload: null,
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbConfigurationViolationException>();
        ex.Which.AssertNgbError(
            NgbConfigurationViolationException.Code,
            "sourceTypeCode",
            "targetTypeCode",
            "relationshipType",
            "derivationCodes");
    }

    [Fact]
    public async Task DeriveAsync_WhenTargetTypeIsUnknown_FailsFast()
    {
        using var host = CreateHost(registerDuplicateDerivation: false);
        await EnsureTablesAsync(host);
        await using var scope = host.Services.CreateAsyncScope();

        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

        var source = await documents.CreateDraftAsync(
            SourceTypeCode,
            Payload(new { display = "Source Alpha" }),
            CancellationToken.None);

        var act = () => documents.DeriveAsync(
            "it_docsvc_unknown",
            source.Id,
            relationshipType: "created_from",
            initialPayload: null,
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DocumentTypeNotFoundException>();
        ex.Which.ErrorCode.Should().Be(DocumentTypeNotFoundException.Code);
        ex.Which.TypeCode.Should().Be("it_docsvc_unknown");
    }

    [Fact]
    public async Task DeriveAsync_WhenRelationshipTypeHasOuterWhitespace_NormalizesAndMatches()
    {
        using var host = CreateHost(registerDuplicateDerivation: false);
        await EnsureTablesAsync(host);
        await using var scope = host.Services.CreateAsyncScope();

        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

        var source = await documents.CreateDraftAsync(
            SourceTypeCode,
            Payload(new { display = "Source Alpha" }),
            CancellationToken.None);

        var derived = await documents.DeriveAsync(
            TargetTypeCode,
            source.Id,
            relationshipType: "  created_from  ",
            initialPayload: null,
            CancellationToken.None);

        derived.Display.Should().Be("Derived draft");
    }

    private IHost CreateHost(bool registerDuplicateDerivation)
        => IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, DocumentServiceDeriveContributor>();
                if (registerDuplicateDerivation)
                    services.AddSingleton<IDefinitionsContributor, DocumentServiceDeriveDuplicateContributor>();

                services.AddScoped<PrefillBetaHandler>();
            });

    private static async Task EnsureTablesAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow.EnsureConnectionOpenAsync(CancellationToken.None);

        await uow.Connection.ExecuteAsync(
            """
            CREATE TABLE IF NOT EXISTS it_docsvc_alpha
            (
                document_id uuid PRIMARY KEY,
                display text NOT NULL
            );

            CREATE TABLE IF NOT EXISTS it_docsvc_beta
            (
                document_id uuid PRIMARY KEY,
                display text NOT NULL,
                memo text NULL
            );

            CREATE TABLE IF NOT EXISTS it_docsvc_beta__draft_lines
            (
                document_id uuid NOT NULL,
                ordinal int NOT NULL,
                note text NULL,
                CONSTRAINT ux_it_docsvc_beta__draft_lines UNIQUE (document_id, ordinal)
            );
            """);
    }

    private static RecordPayload Payload(object fields)
    {
        var element = JsonSerializer.SerializeToElement(fields);
        var dict = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
            dict[property.Name] = property.Value.Clone();

        return new RecordPayload(dict);
    }

    private sealed class DocumentServiceDeriveContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument(SourceTypeCode, d => d.Metadata(new DocumentTypeMetadata(
                TypeCode: SourceTypeCode,
                Tables:
                [
                    new DocumentTableMetadata(
                        TableName: "it_docsvc_alpha",
                        Kind: TableKind.Head,
                        Columns:
                        [
                            new DocumentColumnMetadata("document_id", ColumnType.Guid, Required: true),
                            new DocumentColumnMetadata("display", ColumnType.String, Required: true)
                        ])
                ],
                Presentation: new DocumentPresentationMetadata("IT DocSvc Alpha"),
                Version: new DocumentMetadataVersion(1, "it-tests"))));

            builder.AddDocument(TargetTypeCode, d => d.Metadata(new DocumentTypeMetadata(
                TypeCode: TargetTypeCode,
                Tables:
                [
                    new DocumentTableMetadata(
                        TableName: "it_docsvc_beta",
                        Kind: TableKind.Head,
                        Columns:
                        [
                            new DocumentColumnMetadata("document_id", ColumnType.Guid, Required: true),
                            new DocumentColumnMetadata("display", ColumnType.String, Required: true),
                            new DocumentColumnMetadata("memo", ColumnType.String)
                        ]),
                    new DocumentTableMetadata(
                        TableName: "it_docsvc_beta__draft_lines",
                        Kind: TableKind.Part,
                        PartCode: "lines",
                        Columns:
                        [
                            new DocumentColumnMetadata("document_id", ColumnType.Guid, Required: true),
                            new DocumentColumnMetadata("ordinal", ColumnType.Int32, Required: true),
                            new DocumentColumnMetadata("note", ColumnType.String)
                        ])
                ],
                Presentation: new DocumentPresentationMetadata("IT DocSvc Beta"),
                Version: new DocumentMetadataVersion(1, "it-tests"))));

            builder.AddDocumentDerivation(
                derivationCode: "it_docsvc_alpha.to_beta",
                configure: d => d
                    .Name("Create IT DocSvc Beta")
                    .From(SourceTypeCode)
                    .To(TargetTypeCode)
                    .Relationship("created_from")
                    .Handler<PrefillBetaHandler>());
        }
    }

    private sealed class DocumentServiceDeriveDuplicateContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocumentDerivation(
                derivationCode: "it_docsvc_alpha.to_beta_duplicate",
                configure: d => d
                    .Name("Create IT DocSvc Beta Duplicate")
                    .From(SourceTypeCode)
                    .To(TargetTypeCode)
                    .Relationship("created_from")
                    .Handler<PrefillBetaHandler>());
        }
    }

    private sealed class PrefillBetaHandler(IUnitOfWork uow) : IDocumentDerivationHandler
    {
        public async Task ApplyAsync(DocumentDerivationContext ctx, CancellationToken ct = default)
        {
            uow.EnsureActiveTransaction();
            await uow.EnsureConnectionOpenAsync(ct);

            await uow.Connection.ExecuteAsync(
                new CommandDefinition(
                    """
                    INSERT INTO it_docsvc_beta(document_id, display, memo)
                    VALUES (@document_id, @display, @memo);
                    """,
                    new
                    {
                        document_id = ctx.TargetDraft.Id,
                        display = "Derived draft",
                        memo = "handler memo"
                    },
                    uow.Transaction,
                    cancellationToken: ct));

            await uow.Connection.ExecuteAsync(
                new CommandDefinition(
                    """
                    INSERT INTO it_docsvc_beta__draft_lines(document_id, ordinal, note)
                    VALUES (@document_id, 1, @note);
                    """,
                    new
                    {
                        document_id = ctx.TargetDraft.Id,
                        note = "handler line"
                    },
                    uow.Transaction,
                    cancellationToken: ct));
        }
    }
}
