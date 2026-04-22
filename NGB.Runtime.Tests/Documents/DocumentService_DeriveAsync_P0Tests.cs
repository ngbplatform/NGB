using System.Text.Json;
using FluentAssertions;
using Moq;
using NGB.Accounting.Posting;
using NGB.Contracts.Common;
using NGB.Contracts.Services;
using NGB.Core.Documents;
using NGB.Core.Documents.Exceptions;
using NGB.Metadata.Base;
using NGB.Metadata.Documents.Hybrid;
using NGB.Metadata.Documents.Storage;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.Documents;
using NGB.Persistence.Documents.Universal;
using NGB.Persistence.UnitOfWork;
using NGB.ReferenceRegisters.Contracts;
using NGB.Runtime.Documents;
using NGB.Runtime.Documents.Derivations;
using NGB.Runtime.Documents.Posting;
using NGB.Runtime.Ui;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Documents;

public sealed class DocumentService_DeriveAsync_P0Tests
{
    private const string SourceTypeCode = "test_source";
    private const string TargetTypeCode = "test_target";

    [Fact]
    public async Task DeriveAsync_SingleMatch_DelegatesToDerivationService_AndReturnsDerivedDocument()
    {
        var sourceId = Guid.NewGuid();
        var derivedId = Guid.NewGuid();
        var harness = CreateHarness();

        harness.Documents
            .Setup(x => x.GetAsync(sourceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentRecord
            {
                Id = sourceId,
                TypeCode = SourceTypeCode,
                DateUtc = new DateTime(2026, 4, 17, 0, 0, 0, DateTimeKind.Utc),
                Status = DocumentStatus.Draft,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });

        harness.Derivations
            .Setup(x => x.ListActionsForSourceType(SourceTypeCode))
            .Returns([
                new DocumentDerivationAction(
                    Code: "test_source.to_target",
                    Name: "Create Target",
                    FromTypeCode: SourceTypeCode,
                    ToTypeCode: TargetTypeCode,
                    RelationshipCodes: ["created_from"])
            ]);

        harness.Derivations
            .Setup(x => x.CreateDraftAsync(
                "test_source.to_target",
                sourceId,
                null,
                null,
                null,
                true,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(derivedId);

        harness.Reader
            .Setup(x => x.GetByIdAsync(It.IsAny<DocumentHeadDescriptor>(), derivedId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentHeadRow(
                derivedId,
                DocumentStatus.Draft,
                false,
                "Derived draft",
                new Dictionary<string, object?> { ["display"] = "Derived draft" }));

        var derived = await harness.Service.DeriveAsync(
            TargetTypeCode,
            sourceId,
            relationshipType: "  created_from  ",
            initialPayload: null,
            CancellationToken.None);

        derived.Id.Should().Be(derivedId);
        derived.Display.Should().Be("Derived draft");

        harness.Derivations.Verify(x => x.CreateDraftAsync(
            "test_source.to_target",
            sourceId,
            null,
            null,
            null,
            true,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeriveAsync_WhenInitialPayloadProvided_UpdatesDerivedDraftBeforeReturning()
    {
        var sourceId = Guid.NewGuid();
        var derivedId = Guid.NewGuid();
        var capturedValues = (IReadOnlyList<DocumentHeadValue>?)null;
        var harness = CreateHarness();

        harness.Documents
            .Setup(x => x.GetAsync(sourceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentRecord
            {
                Id = sourceId,
                TypeCode = SourceTypeCode,
                DateUtc = new DateTime(2026, 4, 17, 0, 0, 0, DateTimeKind.Utc),
                Status = DocumentStatus.Draft,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });

        harness.Derivations
            .Setup(x => x.ListActionsForSourceType(SourceTypeCode))
            .Returns([
                new DocumentDerivationAction(
                    Code: "test_source.to_target",
                    Name: "Create Target",
                    FromTypeCode: SourceTypeCode,
                    ToTypeCode: TargetTypeCode,
                    RelationshipCodes: ["created_from"])
            ]);

        harness.Derivations
            .Setup(x => x.CreateDraftAsync(
                "test_source.to_target",
                sourceId,
                null,
                null,
                null,
                true,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(derivedId);

        harness.Documents
            .Setup(x => x.GetForUpdateAsync(derivedId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentRecord
            {
                Id = derivedId,
                TypeCode = TargetTypeCode,
                Number = null,
                DateUtc = new DateTime(2026, 4, 17, 0, 0, 0, DateTimeKind.Utc),
                Status = DocumentStatus.Draft,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });

        harness.Writer
            .Setup(x => x.UpsertHeadAsync(
                It.IsAny<DocumentHeadDescriptor>(),
                derivedId,
                It.IsAny<IReadOnlyList<DocumentHeadValue>>(),
                It.IsAny<CancellationToken>()))
            .Callback<DocumentHeadDescriptor, Guid, IReadOnlyList<DocumentHeadValue>, CancellationToken>((_, _, values, _) => capturedValues = values)
            .Returns(Task.CompletedTask);

        harness.Documents
            .Setup(x => x.UpdateDraftHeaderAsync(
                derivedId,
                null,
                new DateTime(2026, 4, 17, 0, 0, 0, DateTimeKind.Utc),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        harness.Reader
            .Setup(x => x.GetByIdAsync(It.IsAny<DocumentHeadDescriptor>(), derivedId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentHeadRow(
                derivedId,
                DocumentStatus.Draft,
                false,
                "Payload draft",
                new Dictionary<string, object?>
                {
                    ["display"] = "Payload draft",
                    ["memo"] = "payload memo"
                }));

        var payload = new RecordPayload(new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["display"] = JsonSerializer.SerializeToElement("Payload draft"),
            ["memo"] = JsonSerializer.SerializeToElement("payload memo")
        });

        var derived = await harness.Service.DeriveAsync(
            TargetTypeCode,
            sourceId,
            relationshipType: "created_from",
            initialPayload: payload,
            CancellationToken.None);

        derived.Display.Should().Be("Payload draft");
        derived.Payload.Fields.Should().NotBeNull();
        derived.Payload.Fields!["memo"].GetString().Should().Be("payload memo");

        capturedValues.Should().NotBeNull();
        capturedValues!.Single(x => x.ColumnName == "display").Value.Should().Be("Payload draft");
        capturedValues!.Single(x => x.ColumnName == "memo").Value.Should().Be("payload memo");
    }

    [Fact]
    public async Task DeriveAsync_WhenRelationshipTypeIsBlank_ThrowsArgumentRequired()
    {
        var harness = CreateHarness();

        var act = () => harness.Service.DeriveAsync(
            TargetTypeCode,
            Guid.NewGuid(),
            relationshipType: "   ",
            initialPayload: null,
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentRequiredException>();
        ex.Which.ParamName.Should().Be("relationshipType");
    }

    [Fact]
    public async Task GetDerivationActionsAsync_WhenDocumentExists_MapsServiceActionsToContracts()
    {
        var sourceId = Guid.NewGuid();
        var harness = CreateHarness();

        harness.Reader
            .Setup(x => x.GetByIdAsync(It.IsAny<DocumentHeadDescriptor>(), sourceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentHeadRow(
                sourceId,
                DocumentStatus.Draft,
                false,
                "Source draft",
                new Dictionary<string, object?> { ["display"] = "Source draft" }));

        harness.Derivations
            .Setup(x => x.ListActionsForDocumentAsync(sourceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new DocumentDerivationAction(
                    Code: "ab.generate_invoice_draft",
                    Name: "Generate Invoice Draft",
                    FromTypeCode: TargetTypeCode,
                    ToTypeCode: "ab.sales_invoice",
                    RelationshipCodes: ["created_from"])
            ]);

        var actions = await harness.Service.GetDerivationActionsAsync(TargetTypeCode, sourceId, CancellationToken.None);

        actions.Should().BeEquivalentTo([
            new DocumentDerivationActionDto(
                Code: "ab.generate_invoice_draft",
                Name: "Generate Invoice Draft",
                FromTypeCode: TargetTypeCode,
                ToTypeCode: "ab.sales_invoice",
                RelationshipCodes: ["created_from"])
        ]);
    }

    [Fact]
    public async Task DeriveAsync_WhenSourceDocumentMissing_ThrowsDocumentNotFound()
    {
        var sourceId = Guid.NewGuid();
        var harness = CreateHarness();

        harness.Documents
            .Setup(x => x.GetAsync(sourceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentRecord?)null);

        var act = () => harness.Service.DeriveAsync(
            TargetTypeCode,
            sourceId,
            relationshipType: "created_from",
            initialPayload: null,
            CancellationToken.None);

        await act.Should().ThrowAsync<DocumentNotFoundException>();
    }

    [Fact]
    public async Task DeriveAsync_WhenNoMatchingAction_ThrowsDocumentDerivationNotFound()
    {
        var sourceId = Guid.NewGuid();
        var harness = CreateHarness();

        harness.Documents
            .Setup(x => x.GetAsync(sourceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentRecord
            {
                Id = sourceId,
                TypeCode = SourceTypeCode,
                DateUtc = new DateTime(2026, 4, 17, 0, 0, 0, DateTimeKind.Utc),
                Status = DocumentStatus.Draft,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });

        harness.Derivations
            .Setup(x => x.ListActionsForSourceType(SourceTypeCode))
            .Returns([
                new DocumentDerivationAction(
                    Code: "test_source.to_other",
                    Name: "Create Other",
                    FromTypeCode: SourceTypeCode,
                    ToTypeCode: "other_target",
                    RelationshipCodes: ["created_from"])
            ]);

        var act = () => harness.Service.DeriveAsync(
            TargetTypeCode,
            sourceId,
            relationshipType: "created_from",
            initialPayload: null,
            CancellationToken.None);

        await act.Should().ThrowAsync<DocumentDerivationNotFoundException>();
    }

    [Fact]
    public async Task DeriveAsync_WhenNoMatchingActionButInitialPayloadProvided_FallsBackToScaffoldDraft()
    {
        var sourceId = Guid.NewGuid();
        var derivedId = Guid.NewGuid();
        var capturedValues = (IReadOnlyList<DocumentHeadValue>?)null;
        var harness = CreateHarness();

        harness.Documents
            .Setup(x => x.GetAsync(sourceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentRecord
            {
                Id = sourceId,
                TypeCode = SourceTypeCode,
                DateUtc = new DateTime(2026, 4, 17, 0, 0, 0, DateTimeKind.Utc),
                Status = DocumentStatus.Draft,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });

        harness.Derivations
            .Setup(x => x.ListActionsForSourceType(SourceTypeCode))
            .Returns([
                new DocumentDerivationAction(
                    Code: "test_source.to_other",
                    Name: "Create Other",
                    FromTypeCode: SourceTypeCode,
                    ToTypeCode: "other_target",
                    RelationshipCodes: ["created_from"])
            ]);

        harness.Drafts
            .Setup(x => x.CreateDraftAsync(
                TargetTypeCode,
                null,
                It.IsAny<DateTime>(),
                manageTransaction: false,
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(derivedId);

        harness.Writer
            .Setup(x => x.UpsertHeadAsync(
                It.IsAny<DocumentHeadDescriptor>(),
                derivedId,
                It.IsAny<IReadOnlyList<DocumentHeadValue>>(),
                It.IsAny<CancellationToken>()))
            .Callback<DocumentHeadDescriptor, Guid, IReadOnlyList<DocumentHeadValue>, CancellationToken>((_, _, values, _) => capturedValues = values)
            .Returns(Task.CompletedTask);

        harness.Reader
            .Setup(x => x.GetByIdAsync(It.IsAny<DocumentHeadDescriptor>(), derivedId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentHeadRow(
                derivedId,
                DocumentStatus.Draft,
                false,
                "Scaffold draft",
                new Dictionary<string, object?>
                {
                    ["display"] = "Scaffold draft",
                    ["memo"] = "payload memo"
                }));

        var payload = new RecordPayload(new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["display"] = JsonSerializer.SerializeToElement("Scaffold draft"),
            ["memo"] = JsonSerializer.SerializeToElement("payload memo")
        });

        var derived = await harness.Service.DeriveAsync(
            TargetTypeCode,
            sourceId,
            relationshipType: "created_from",
            initialPayload: payload,
            CancellationToken.None);

        derived.Id.Should().Be(derivedId);
        derived.Display.Should().Be("Scaffold draft");
        derived.Payload.Fields.Should().NotBeNull();
        derived.Payload.Fields!["memo"].GetString().Should().Be("payload memo");

        capturedValues.Should().NotBeNull();
        capturedValues!.Single(x => x.ColumnName == "display").Value.Should().Be("Scaffold draft");
        capturedValues!.Single(x => x.ColumnName == "memo").Value.Should().Be("payload memo");

        harness.Derivations.Verify(x => x.CreateDraftAsync(
            It.IsAny<string>(),
            It.IsAny<Guid>(),
            It.IsAny<IReadOnlyList<Guid>?>(),
            It.IsAny<DateTime?>(),
            It.IsAny<string?>(),
            It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeriveAsync_WhenMultipleMatchesExist_ThrowsConfigurationViolation()
    {
        var sourceId = Guid.NewGuid();
        var harness = CreateHarness();

        harness.Documents
            .Setup(x => x.GetAsync(sourceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentRecord
            {
                Id = sourceId,
                TypeCode = SourceTypeCode,
                DateUtc = new DateTime(2026, 4, 17, 0, 0, 0, DateTimeKind.Utc),
                Status = DocumentStatus.Draft,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });

        harness.Derivations
            .Setup(x => x.ListActionsForSourceType(SourceTypeCode))
            .Returns([
                new DocumentDerivationAction("test_source.to_target_1", "Create Target 1", SourceTypeCode, TargetTypeCode, ["created_from"]),
                new DocumentDerivationAction("test_source.to_target_2", "Create Target 2", SourceTypeCode, TargetTypeCode, ["created_from"])
            ]);

        var act = () => harness.Service.DeriveAsync(
            TargetTypeCode,
            sourceId,
            relationshipType: "created_from",
            initialPayload: null,
            CancellationToken.None);

        await act.Should().ThrowAsync<NgbConfigurationViolationException>();
    }

    private static Harness CreateHarness()
    {
        var uow = CreateUowMock();
        var documents = new Mock<IDocumentRepository>(MockBehavior.Strict);
        var drafts = new Mock<IDocumentDraftService>(MockBehavior.Strict);
        var registry = CreateRegistryMock(BuildTargetMeta());
        var reader = new Mock<IDocumentReader>(MockBehavior.Strict);
        var partsReader = new Mock<IDocumentPartsReader>(MockBehavior.Strict);
        var partsWriter = new Mock<IDocumentPartsWriter>(MockBehavior.Strict);
        var writer = new Mock<IDocumentWriter>(MockBehavior.Strict);
        var posting = new Mock<IDocumentPostingService>(MockBehavior.Strict);
        var derivations = new Mock<IDocumentDerivationService>(MockBehavior.Strict);
        var postingActionResolver = new Mock<IDocumentPostingActionResolver>(MockBehavior.Strict);
        var opregPostingActionResolver = new Mock<IDocumentOperationalRegisterPostingActionResolver>(MockBehavior.Strict);
        var refregPostingActionResolver = new Mock<IDocumentReferenceRegisterPostingActionResolver>(MockBehavior.Strict);
        var relationshipGraph = new Mock<IDocumentRelationshipGraphReadService>(MockBehavior.Strict);

        postingActionResolver
            .Setup(x => x.TryResolve(It.IsAny<DocumentRecord>()))
            .Returns((Func<IAccountingPostingContext, CancellationToken, Task>?)null);

        opregPostingActionResolver
            .Setup(x => x.TryResolve(It.IsAny<DocumentRecord>()))
            .Returns((Func<IOperationalRegisterMovementsBuilder, CancellationToken, Task>?)null);

        refregPostingActionResolver
            .Setup(x => x.TryResolve(It.IsAny<DocumentRecord>()))
            .Returns((Func<IReferenceRegisterRecordsBuilder, ReferenceRegisterWriteOperation, CancellationToken, Task>?)null);

        var service = new DocumentService(
            uow.Object,
            documents.Object,
            drafts.Object,
            registry.Object,
            reader.Object,
            partsReader.Object,
            partsWriter.Object,
            writer.Object,
            posting.Object,
            derivations.Object,
            postingActionResolver.Object,
            opregPostingActionResolver.Object,
            refregPostingActionResolver.Object,
            [],
            relationshipGraph.Object,
            NoOpReferencePayloadEnricher.Instance,
            []);

        return new Harness(service, documents, drafts, derivations, reader, writer);
    }

    private static Mock<IUnitOfWork> CreateUowMock()
    {
        var uow = new Mock<IUnitOfWork>(MockBehavior.Strict);
        uow.SetupGet(x => x.HasActiveTransaction).Returns(false);
        uow.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        uow.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        uow.Setup(x => x.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        uow.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask);
        return uow;
    }

    private static Mock<IDocumentTypeRegistry> CreateRegistryMock(DocumentTypeMetadata targetMeta)
    {
        var reg = new Mock<IDocumentTypeRegistry>(MockBehavior.Strict);
        reg.Setup(x => x.TryGet(It.IsAny<string>()))
            .Returns((string typeCode) =>
                string.Equals(typeCode, targetMeta.TypeCode, StringComparison.OrdinalIgnoreCase)
                    ? targetMeta
                    : null);
        reg.Setup(x => x.GetAll()).Returns([targetMeta]);
        return reg;
    }

    private static DocumentTypeMetadata BuildTargetMeta()
        => new(
            TypeCode: TargetTypeCode,
            Tables:
            [
                new DocumentTableMetadata(
                    TableName: "doc_test_target",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new DocumentColumnMetadata("document_id", ColumnType.Guid, Required: true),
                        new DocumentColumnMetadata("display", ColumnType.String, Required: true),
                        new DocumentColumnMetadata("memo", ColumnType.String)
                    ])
            ],
            Presentation: new DocumentPresentationMetadata(TargetTypeCode),
            Version: new DocumentMetadataVersion(1, "tests"));

    private sealed record Harness(
        DocumentService Service,
        Mock<IDocumentRepository> Documents,
        Mock<IDocumentDraftService> Drafts,
        Mock<IDocumentDerivationService> Derivations,
        Mock<IDocumentReader> Reader,
        Mock<IDocumentWriter> Writer);
}
