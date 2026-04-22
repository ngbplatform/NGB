using FluentAssertions;
using Moq;
using NGB.Accounting.Posting;
using NGB.OperationalRegisters.Contracts;
using NGB.ReferenceRegisters.Contracts;
using NGB.Contracts.Metadata;
using NGB.Core.Documents;
using NGB.Core.Documents.Exceptions;
using NGB.Core.Documents.Relationships.Graph;
using NGB.Metadata.Base;
using NGB.Metadata.Documents.Hybrid;
using NGB.Metadata.Documents.Storage;
using NGB.Persistence.Documents;
using NGB.Persistence.Documents.Universal;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Documents;
using NGB.Runtime.Documents.Derivations;
using NGB.Runtime.Documents.Posting;
using NGB.Runtime.Ui;
using Xunit;
using DocumentStatus = NGB.Core.Documents.DocumentStatus;

namespace NGB.Runtime.Tests.Documents;

public sealed class DocumentService_Delegation_And_Scaffolds_P0Tests
{
    private const string TypeCode = "test_doc";

    [Fact]
    public async Task RepostAsync_ResolvesConfiguredPostingAction_AndDelegatesToPostingService()
    {
        // Arrange
        var id = Guid.NewGuid();
        var meta = BuildMeta(TypeCode);
        var uow = CreateUowMock();
        var docs = new Mock<IDocumentRepository>(MockBehavior.Strict);
        var drafts = new Mock<IDocumentDraftService>(MockBehavior.Strict);
        var reg = CreateRegistryMock(meta);
        var reader = new Mock<IDocumentReader>(MockBehavior.Strict);
        var partsReader = new Mock<IDocumentPartsReader>(MockBehavior.Strict);
        var partsWriter = new Mock<IDocumentPartsWriter>(MockBehavior.Strict);
        var writer = new Mock<IDocumentWriter>(MockBehavior.Strict);
        var posting = new Mock<IDocumentPostingService>(MockBehavior.Strict);
        var derivations = new Mock<IDocumentDerivationService>(MockBehavior.Strict);
        var postingActionResolver = new Mock<IDocumentPostingActionResolver>(MockBehavior.Strict);
        var opregPostingActionResolver = new Mock<IDocumentOperationalRegisterPostingActionResolver>(MockBehavior.Strict);
        var refregPostingActionResolver = new Mock<IDocumentReferenceRegisterPostingActionResolver>(MockBehavior.Strict);

        var doc = new DocumentRecord
        {
            Id = id,
            TypeCode = TypeCode,
            Number = null,
            DateUtc = new DateTime(2026, 2, 22, 0, 0, 0, DateTimeKind.Utc),
            Status = DocumentStatus.Draft,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };

        docs.Setup(x => x.GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(doc);

        var invoked = false;
        Func<IAccountingPostingContext, CancellationToken, Task> action = (_, _) =>
        {
            invoked = true;
            return Task.CompletedTask;
        };

        postingActionResolver.Setup(x => x.TryResolve(It.IsAny<DocumentRecord>())).Returns(action);

        Func<IAccountingPostingContext, CancellationToken, Task>? repostDelegate = null;
        posting
            .Setup(x => x.RepostAsync(id, It.IsAny<Func<IAccountingPostingContext, CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, Func<IAccountingPostingContext, CancellationToken, Task>, CancellationToken>((_, del, _) => repostDelegate = del)
            .Returns(Task.CompletedTask);

        // Repost returns GetById => return a minimal row
        reader.Setup(x => x.GetByIdAsync(It.IsAny<DocumentHeadDescriptor>(), id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentHeadRow(id, DocumentStatus.Draft, false, "x", new Dictionary<string, object?> { ["display"] = "x" }));

        var relationshipGraph = new Mock<IDocumentRelationshipGraphReadService>(MockBehavior.Strict);

        var svc = new DocumentService(
            uow.Object,
            docs.Object,
            drafts.Object,
            reg.Object,
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

        // Act
        var result = await svc.RepostAsync(TypeCode, id, CancellationToken.None);

        // Assert
        result.Id.Should().Be(id);
        posting.Verify(x => x.RepostAsync(id, It.IsAny<Func<IAccountingPostingContext, CancellationToken, Task>>(), It.IsAny<CancellationToken>()), Times.Once);

        repostDelegate.Should().NotBeNull();
        await repostDelegate!(Mock.Of<IAccountingPostingContext>(), CancellationToken.None);
        invoked.Should().BeTrue();
    }

    [Fact]
    public async Task RepostAsync_WhenDocumentDoesNotExist_ThrowsDocumentNotFound()
    {
        // Arrange
        var id = Guid.NewGuid();
        var svc = CreateSut(BuildMeta(TypeCode), docsSetup: d =>
        {
            d.Setup(x => x.GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((DocumentRecord?)null);
        });

        // Act
        var act = () => svc.RepostAsync(TypeCode, id, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<DocumentNotFoundException>();
    }

    [Fact]
    public async Task ExecuteActionAsync_WhenGenericActionsAreDisabled_ThrowsValidationError()
    {
        // Arrange
        var id = Guid.NewGuid();
        var meta = BuildMeta(TypeCode);
        var svc = CreateSut(meta);

        // Act
        var act = () => svc.ExecuteActionAsync(TypeCode, id, "noop", CancellationToken.None);

        // Assert
        var ex = await act.Should().ThrowAsync<DocumentActionsNotSupportedException>();
        ex.Which.ErrorCode.Should().Be(DocumentActionsNotSupportedException.ErrorCodeConst);
        ex.Which.Context["documentTypeCode"].Should().Be(TypeCode);
        ex.Which.Context["actionCode"].Should().Be("noop");
    }

    [Fact]
    public async Task GetTypeMetadataAsync_DisablesGenericActionsInCapabilities()
    {
        // Arrange
        var svc = CreateSut(BuildMeta(TypeCode));

        // Act
        var dto = await svc.GetTypeMetadataAsync(TypeCode, CancellationToken.None);

        // Assert
        dto.Capabilities.Should().NotBeNull();
        dto.Capabilities!.SupportsActions.Should().BeFalse();
    }

    [Fact]
    public async Task GetEffectsAsync_IsScaffold_ReturnsEmptyArrays()
    {
        // Arrange
        var id = Guid.NewGuid();
        var svc = CreateSut(BuildMeta(TypeCode),
            docsSetup: d =>
            {
                d.Setup(x => x.GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(new DocumentRecord
                {
                    Id = id,
                    TypeCode = TypeCode,
                    Number = null,
                    DateUtc = new DateTime(2026, 2, 22, 0, 0, 0, DateTimeKind.Utc),
                    Status = DocumentStatus.Draft,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow,
                });
            },
            readerSetup: r =>
            {
                r.Setup(x => x.GetByIdAsync(It.IsAny<DocumentHeadDescriptor>(), id, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new DocumentHeadRow(id, DocumentStatus.Draft, false, "x", new Dictionary<string, object?> { ["display"] = "x" }));
            });

        // Act
        var effects = await svc.GetEffectsAsync(TypeCode, id, limit: 100, CancellationToken.None);

        // Assert
        effects.AccountingEntries.Should().BeEmpty();
        effects.OperationalRegisterMovements.Should().BeEmpty();
        effects.ReferenceRegisterWrites.Should().BeEmpty();
        effects.Ui.Should().NotBeNull();
    }

    [Fact]
    public async Task GetRelationshipGraphAsync_IsScaffold_ReturnsSingleNodeGraph()
    {
        // Arrange
        var id = Guid.NewGuid();
        var meta = BuildMeta(TypeCode);
        var svc = CreateSut(meta, readerSetup: r =>
        {
            r.Setup(x => x.GetByIdAsync(It.IsAny<DocumentHeadDescriptor>(), id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DocumentHeadRow(
                    id,
                    DocumentStatus.Draft,
                    false,
                    "My doc",
                    new Dictionary<string, object?> { ["display"] = "My doc" }));
        },
        graphSetup: g =>
        {
            g.Setup(x => x.GetGraphAsync(
                    It.Is<DocumentRelationshipGraphRequest>(r => r.RootDocumentId == id && r.MaxDepth == 3 && r.MaxNodes == 100),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DocumentRelationshipGraph(
                    id,
                    new[]
                    {
                        new DocumentRelationshipGraphNode(
                            id,
                            TypeCode,
                            Number: null,
                            DateUtc: new DateTime(2026, 2, 22, 0, 0, 0, DateTimeKind.Utc),
                            Status: DocumentStatus.Draft,
                            Depth: 0)
                    },
                    Array.Empty<DocumentRelationshipGraphEdge>()));
        });

        // Act
        var graph = await svc.GetRelationshipGraphAsync(TypeCode, id, depth: 3, maxNodes: 100, CancellationToken.None);

        // Assert
        graph.Nodes.Should().HaveCount(1);
        graph.Edges.Should().BeEmpty();

        var node = graph.Nodes.Single();
        node.Kind.Should().Be(EntityKind.Document);
        node.TypeCode.Should().Be(TypeCode);
        node.EntityId.Should().Be(id);
        node.NodeId.Should().Be($"doc:{TypeCode}:{id}");
        node.Title.Should().Be("My doc");
        node.DocumentStatus.Should().Be(Contracts.Metadata.DocumentStatus.Draft);
    }

    [Fact]
    public async Task GetRelationshipGraphAsync_BatchesNonRootHeadRowsAcrossTypes()
    {
        // Arrange
        const string childTypeA = "test_child_a";
        const string childTypeB = "test_child_b";

        var rootId = Guid.NewGuid();
        var childAId = Guid.NewGuid();
        var childBId = Guid.NewGuid();
        var rootMeta = BuildMeta(TypeCode, tableName: "doc_test_root");
        var childMetaA = BuildMeta(
            childTypeA,
            tableName: "doc_test_child_a",
            presentation: new DocumentPresentationMetadata(childTypeA, AmountField: "total_due"),
            additionalColumns:
            [
                new DocumentColumnMetadata("total_due", ColumnType.Decimal)
            ]);
        var childMetaB = BuildMeta(
            childTypeB,
            tableName: "doc_test_child_b",
            presentation: new DocumentPresentationMetadata(childTypeB, AmountField: "applied_total"),
            additionalColumns:
            [
                new DocumentColumnMetadata("applied_total", ColumnType.Decimal)
            ]);
        var batchedHeadReadObserved = false;

        var svc = CreateSut(
            rootMeta,
            readerSetup: r =>
            {
                r.Setup(x => x.GetByIdAsync(It.Is<DocumentHeadDescriptor>(h => h.TypeCode == TypeCode), rootId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new DocumentHeadRow(
                        rootId,
                        DocumentStatus.Draft,
                        false,
                        "Root doc",
                        new Dictionary<string, object?> { ["display"] = "Root doc" }));

                r.Setup(x => x.GetHeadRowsByIdsAcrossTypesAsync(
                        It.Is<IReadOnlyList<DocumentHeadDescriptor>>(heads =>
                            heads.Count == 2
                            && heads.Any(h => h.TypeCode == childTypeA && h.HeadTableName == "doc_test_child_a")
                            && heads.Any(h => h.TypeCode == childTypeB && h.HeadTableName == "doc_test_child_b")),
                        It.Is<IReadOnlyList<Guid>>(ids =>
                            ids.Count == 2
                            && ids.Contains(childAId)
                            && ids.Contains(childBId)),
                        It.IsAny<CancellationToken>()))
                    .Callback(() => batchedHeadReadObserved = true)
                    .ReturnsAsync(
                    [
                        new DocumentHeadRow(
                            childAId,
                            DocumentStatus.Posted,
                            false,
                            "Child A",
                            new Dictionary<string, object?> { ["display"] = "Child A", ["total_due"] = 125.50m }),
                        new DocumentHeadRow(
                            childBId,
                            DocumentStatus.Draft,
                            false,
                            "Child B",
                            new Dictionary<string, object?> { ["display"] = "Child B", ["applied_total"] = 75m })
                    ]);
            },
            graphSetup: g =>
            {
                g.Setup(x => x.GetGraphAsync(
                        It.Is<DocumentRelationshipGraphRequest>(r => r.RootDocumentId == rootId && r.MaxDepth == 2 && r.MaxNodes == 100),
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new DocumentRelationshipGraph(
                        rootId,
                        new[]
                        {
                            new DocumentRelationshipGraphNode(rootId, TypeCode, "ROOT-1", new DateTime(2026, 2, 20, 0, 0, 0, DateTimeKind.Utc), DocumentStatus.Draft, 0),
                            new DocumentRelationshipGraphNode(childAId, childTypeA, "A-1", new DateTime(2026, 2, 21, 0, 0, 0, DateTimeKind.Utc), DocumentStatus.Posted, 1),
                            new DocumentRelationshipGraphNode(childBId, childTypeB, "B-1", new DateTime(2026, 2, 22, 0, 0, 0, DateTimeKind.Utc), DocumentStatus.Draft, 1)
                        },
                        new[]
                        {
                            new DocumentRelationshipGraphEdge(Guid.NewGuid(), rootId, childAId, "based_on", "based_on", new DateTime(2026, 2, 23, 0, 0, 0, DateTimeKind.Utc)),
                            new DocumentRelationshipGraphEdge(Guid.NewGuid(), rootId, childBId, "based_on", "based_on", new DateTime(2026, 2, 23, 0, 0, 0, DateTimeKind.Utc))
                        }));
            },
            additionalMetas: [childMetaA, childMetaB]);

        // Act
        var graph = await svc.GetRelationshipGraphAsync(TypeCode, rootId, depth: 2, maxNodes: 100, CancellationToken.None);

        // Assert
        batchedHeadReadObserved.Should().BeTrue();

        graph.Nodes.Should().HaveCount(3);
        graph.Nodes.Should().ContainSingle(n => n.EntityId == childAId && n.Title == "Child A" && n.Amount == 125.50m);
        graph.Nodes.Should().ContainSingle(n => n.EntityId == childBId && n.Title == "Child B" && n.Amount == 75m);
        graph.Edges.Should().HaveCount(2);
    }

    [Fact]
    public async Task Post_Unpost_MarkAndUnmarkForDeletion_DelegateToPostingService_AndReturnGetById()
    {
        // Arrange
        var id = Guid.NewGuid();
        var meta = BuildMeta(TypeCode);
        var uow = CreateUowMock();
        var docs = new Mock<IDocumentRepository>(MockBehavior.Strict);
        var drafts = new Mock<IDocumentDraftService>(MockBehavior.Strict);
        var reg = CreateRegistryMock(meta);
        var reader = new Mock<IDocumentReader>(MockBehavior.Strict);
        var partsReader = new Mock<IDocumentPartsReader>(MockBehavior.Strict);
        var partsWriter = new Mock<IDocumentPartsWriter>(MockBehavior.Strict);
        var writer = new Mock<IDocumentWriter>(MockBehavior.Strict);
        var posting = new Mock<IDocumentPostingService>(MockBehavior.Strict);
        var derivations = new Mock<IDocumentDerivationService>(MockBehavior.Strict);
        var postingActionResolver = new Mock<IDocumentPostingActionResolver>(MockBehavior.Strict);
        var opregPostingActionResolver = new Mock<IDocumentOperationalRegisterPostingActionResolver>(MockBehavior.Strict);
        var refregPostingActionResolver = new Mock<IDocumentReferenceRegisterPostingActionResolver>(MockBehavior.Strict);

        posting.Setup(x => x.PostAsync(id, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        posting.Setup(x => x.UnpostAsync(id, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        posting.Setup(x => x.MarkForDeletionAsync(id, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        posting.Setup(x => x.UnmarkForDeletionAsync(id, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        reader.Setup(x => x.GetByIdAsync(It.IsAny<DocumentHeadDescriptor>(), id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentHeadRow(
                id,
                DocumentStatus.Draft,
                false,
                "x",
                new Dictionary<string, object?> { ["display"] = "x" }));

        var relationshipGraph = new Mock<IDocumentRelationshipGraphReadService>(MockBehavior.Strict);

        var svc = new DocumentService(
            uow.Object,
            docs.Object,
            drafts.Object,
            reg.Object,
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

        // Act
        var post = await svc.PostAsync(TypeCode, id, CancellationToken.None);
        var unpost = await svc.UnpostAsync(TypeCode, id, CancellationToken.None);
        var mark = await svc.MarkForDeletionAsync(TypeCode, id, CancellationToken.None);
        var unmark = await svc.UnmarkForDeletionAsync(TypeCode, id, CancellationToken.None);

        // Assert
        post.Id.Should().Be(id);
        unpost.Id.Should().Be(id);
        mark.Id.Should().Be(id);
        unmark.Id.Should().Be(id);

        posting.Verify(x => x.PostAsync(id, It.IsAny<CancellationToken>()), Times.Once);
        posting.Verify(x => x.UnpostAsync(id, It.IsAny<CancellationToken>()), Times.Once);
        posting.Verify(x => x.MarkForDeletionAsync(id, It.IsAny<CancellationToken>()), Times.Once);
        posting.Verify(x => x.UnmarkForDeletionAsync(id, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static DocumentService CreateSut(
        DocumentTypeMetadata meta,
        Action<Mock<IDocumentRepository>>? docsSetup = null,
        Action<Mock<IDocumentReader>>? readerSetup = null,
        Action<Mock<IDocumentRelationshipGraphReadService>>? graphSetup = null,
        IReadOnlyList<DocumentTypeMetadata>? additionalMetas = null)
    {
        var uow = CreateUowMock();
        var docs = new Mock<IDocumentRepository>(MockBehavior.Strict);
        var drafts = new Mock<IDocumentDraftService>(MockBehavior.Strict);
        var reg = CreateRegistryMock([meta, .. (additionalMetas ?? [])]);
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

        // Default UI effects path uses TryResolve(...) on posting action resolvers.
        // Provide safe defaults for Strict mocks so scaffold tests do not fail.
        postingActionResolver
            .Setup(x => x.TryResolve(It.IsAny<DocumentRecord>()))
            .Returns((Func<IAccountingPostingContext, CancellationToken, Task>?)null);

        opregPostingActionResolver
            .Setup(x => x.TryResolve(It.IsAny<DocumentRecord>()))
            .Returns((Func<IOperationalRegisterMovementsBuilder, CancellationToken, Task>?)null);

        refregPostingActionResolver
            .Setup(x => x.TryResolve(It.IsAny<DocumentRecord>()))
            .Returns((Func<IReferenceRegisterRecordsBuilder, ReferenceRegisterWriteOperation, CancellationToken, Task>?)null);

        docsSetup?.Invoke(docs);
        readerSetup?.Invoke(reader);
        graphSetup?.Invoke(relationshipGraph);

        return new DocumentService(
            uow.Object,
            docs.Object,
            drafts.Object,
            reg.Object,
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
    }

    private static DocumentTypeMetadata BuildMeta(
        string typeCode,
        string tableName = "doc_test",
        DocumentPresentationMetadata? presentation = null,
        params DocumentColumnMetadata[] additionalColumns)
        => new(
            TypeCode: typeCode,
            Tables:
            [
                new DocumentTableMetadata(
                    TableName: tableName,
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String, Required: true),
                        .. additionalColumns
                    ])
            ],
            Presentation: presentation ?? new DocumentPresentationMetadata(typeCode),
            Version: new DocumentMetadataVersion(1, "tests"));

    private static Mock<IDocumentTypeRegistry> CreateRegistryMock(params DocumentTypeMetadata[] metas)
    {
        var reg = new Mock<IDocumentTypeRegistry>(MockBehavior.Strict);
        reg.Setup(x => x.TryGet(It.IsAny<string>()))
            .Returns((string typeCode) => metas.FirstOrDefault(x => string.Equals(x.TypeCode, typeCode, StringComparison.OrdinalIgnoreCase)));
        reg.Setup(x => x.GetAll()).Returns(metas);
        return reg;
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
}
