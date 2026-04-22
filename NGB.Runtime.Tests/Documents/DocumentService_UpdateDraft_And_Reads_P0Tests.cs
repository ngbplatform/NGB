using System.Text.Json;
using FluentAssertions;
using Moq;
using NGB.Contracts.Common;
using NGB.Core.Documents;
using NGB.Core.Documents.Exceptions;
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
using NGB.Runtime.Documents.Workflow;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Documents;

public sealed class DocumentService_UpdateDraft_And_Reads_P0Tests
{
    private const string TypeCode = "test_doc";

    [Fact]
    public async Task GetByIdAsync_WhenNotFound_ThrowsDocumentNotFound()
    {
        // Arrange
        var meta = BuildMetaWithHead(TypeCode, "doc_test", [Col("document_id", ColumnType.Guid, true), Col("display", ColumnType.String, true)]);
        var svc = CreateSut(meta, readerSetup: r =>
        {
            r.Setup(x => x.GetByIdAsync(It.IsAny<DocumentHeadDescriptor>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((DocumentHeadRow?)null);
        });

        // Act
        var act = () => svc.GetByIdAsync(TypeCode, Guid.NewGuid(), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<DocumentNotFoundException>();
    }

    [Fact]
    public async Task GetPageAsync_WhenUnknownFilterProvided_ThrowsEarly()
    {
        // Arrange
        var meta = BuildMetaWithHead(TypeCode, "doc_test", [
            Col("document_id", ColumnType.Guid, true),
            Col("display", ColumnType.String, true),
            Col("party_id", ColumnType.Guid),
        ]);

        var svc = CreateSut(meta);

        // Act
        var act = () => svc.GetPageAsync(
            TypeCode,
            new PageRequestDto(
                Offset: 0,
                Limit: 10,
                Search: null,
                Filters: new Dictionary<string, string> { ["unknown"] = "x" }),
            CancellationToken.None);

        // Assert
        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("filters");
        ex.Which.Message.Should().Be("Filter 'Unknown' is not available for this document list.");
    }

    [Fact]
    public async Task GetPageAsync_WhenMultiValueFilterProvided_BuildsTypedQueryWithResolvedHeadColumn()
    {
        // Arrange
        var leaseId1 = Guid.NewGuid();
        var leaseId2 = Guid.NewGuid();
        var meta = BuildMetaWithHead(
            TypeCode,
            "doc_test",
            [
                Col("document_id", ColumnType.Guid, true),
                Col("display", ColumnType.String, true),
                Col("lease_id", ColumnType.Guid)
            ],
            listFilters:
            [
                new DocumentListFilterMetadata(
                    Key: "lease_id",
                    Label: "Lease",
                    Type: ColumnType.Guid,
                    IsMulti: true)
            ]);

        var row = new DocumentHeadRow(
            Guid.NewGuid(),
            DocumentStatus.Draft,
            false,
            "Document #1",
            new Dictionary<string, object?> { ["display"] = "Document #1", ["lease_id"] = leaseId1 });

        DocumentQuery? capturedCountQuery = null;
        DocumentQuery? capturedPageQuery = null;

        var svc = CreateSut(meta, readerSetup: r =>
        {
            r.Setup(x => x.CountAsync(It.IsAny<DocumentHeadDescriptor>(), It.IsAny<DocumentQuery>(), It.IsAny<CancellationToken>()))
                .Callback<DocumentHeadDescriptor, DocumentQuery, CancellationToken>((_, query, _) => capturedCountQuery = query)
                .ReturnsAsync(1);

            r.Setup(x => x.GetPageAsync(
                    It.IsAny<DocumentHeadDescriptor>(),
                    It.IsAny<DocumentQuery>(),
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .Callback<DocumentHeadDescriptor, DocumentQuery, int, int, CancellationToken>((_, query, _, _, _) => capturedPageQuery = query)
                .ReturnsAsync([row]);
        });

        // Act
        var page = await svc.GetPageAsync(
            TypeCode,
            new PageRequestDto(
                Offset: 0,
                Limit: 10,
                Search: null,
                Filters: new Dictionary<string, string>
                {
                    ["lease_id"] = $"{leaseId1},{leaseId2}"
                }),
            CancellationToken.None);

        // Assert
        page.Items.Should().ContainSingle();

        capturedCountQuery.Should().NotBeNull();
        capturedPageQuery.Should().NotBeNull();

        var countFilter = capturedCountQuery!.Filters.Should().ContainSingle().Subject;
        countFilter.Key.Should().Be("lease_id");
        countFilter.Values.Should().Equal(leaseId1.ToString(), leaseId2.ToString());
        countFilter.ValueType.Should().Be(ColumnType.Guid);
        countFilter.HeadColumnName.Should().Be("lease_id");

        var pageFilter = capturedPageQuery!.Filters.Should().ContainSingle().Subject;
        pageFilter.Key.Should().Be("lease_id");
        pageFilter.Values.Should().Equal(leaseId1.ToString(), leaseId2.ToString());
        pageFilter.ValueType.Should().Be(ColumnType.Guid);
        pageFilter.HeadColumnName.Should().Be("lease_id");
    }

    [Fact]
    public async Task UpdateDraftAsync_PartialUpdate_MergesRequiredColumns_FromExistingRow()
    {
        // Arrange
        var id = Guid.NewGuid();
        var meta = BuildMetaWithHead(TypeCode, "doc_test", [
            Col("document_id", ColumnType.Guid, true),
            Col("display", ColumnType.String, true),
            Col("party_id", ColumnType.Guid, true),
            Col("memo", ColumnType.String),
        ]);

        var locked = new DocumentRecord
        {
            Id = id,
            TypeCode = TypeCode,
            Number = null,
            DateUtc = new DateTime(2026, 2, 22, 0, 0, 0, DateTimeKind.Utc),
            Status = DocumentStatus.Draft,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            PostedAtUtc = null,
            MarkedForDeletionAtUtc = null
        };

        var existingParty = Guid.NewGuid();
        var existingRow = new DocumentHeadRow(
            id,
            DocumentStatus.Draft,
            false,
            "Lease#1",
            new Dictionary<string, object?>
            {
                ["display"] = "Lease#1",
                ["party_id"] = existingParty,
                ["memo"] = "old"
            });

        IReadOnlyList<DocumentHeadValue>? captured = null;

        var svc = CreateSut(
            meta,
            lockedDoc: locked,
            existingRow: existingRow,
            writerSetup: w =>
            {
                w.Setup(x => x.UpsertHeadAsync(
                        It.IsAny<DocumentHeadDescriptor>(),
                        id,
                        It.IsAny<IReadOnlyList<DocumentHeadValue>>(),
                        It.IsAny<CancellationToken>()))
                    .Callback<DocumentHeadDescriptor, Guid, IReadOnlyList<DocumentHeadValue>, CancellationToken>((_, _, v, _) => captured = v)
                    .Returns(Task.CompletedTask);
            });

        var payload = new RecordPayload(new Dictionary<string, JsonElement>
        {
            // partial update: only memo
            ["memo"] = JsonSerializer.SerializeToElement("new")
        });

        // Act
        var updated = await svc.UpdateDraftAsync(TypeCode, id, payload, CancellationToken.None);

        // Assert
        updated.Id.Should().Be(id);
        captured.Should().NotBeNull();
        var byName = captured!.ToDictionary(x => x.ColumnName, StringComparer.OrdinalIgnoreCase);
        byName["memo"].Value.Should().Be("new");
        byName["display"].Value.Should().Be("Lease#1"); // merged required
        byName["party_id"].Value.Should().Be(existingParty); // merged required
    }

    [Fact]
    public async Task UpdateDraftAsync_WhenRequiredFieldProvidedAsNull_Throws()
    {
        // Arrange
        var id = Guid.NewGuid();
        var meta = BuildMetaWithHead(TypeCode, "doc_test", [
            Col("document_id", ColumnType.Guid, true),
            Col("display", ColumnType.String, true),
        ]);

        var svc = CreateSut(
            meta,
            lockedDoc: new DocumentRecord
            {
                Id = id,
                TypeCode = TypeCode,
                Number = null,
                DateUtc = new DateTime(2026, 2, 22, 0, 0, 0, DateTimeKind.Utc),
                Status = DocumentStatus.Draft,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            },
            existingRow: new DocumentHeadRow(id, DocumentStatus.Draft, false, "x", new Dictionary<string, object?> { ["display"] = "x" }));

        var payload = new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["display"] = JsonSerializer.SerializeToElement<object?>(null)
        });

        // Act
        var act = () => svc.UpdateDraftAsync(TypeCode, id, payload, CancellationToken.None);

        // Assert
        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("payload.Fields.display");
        ex.Which.Message.Should().Contain("required");
    }

    [Fact]
    public async Task UpdateDraftAsync_WhenLockedDocumentIsMarkedForDeletion_ThrowsDomainException()
    {
        // Arrange
        var id = Guid.NewGuid();
        var meta = BuildMetaWithHead(TypeCode, "doc_test", [
            Col("document_id", ColumnType.Guid, true),
            Col("display", ColumnType.String, true),
        ]);

        var svc = CreateSut(
            meta,
            lockedDoc: new DocumentRecord
            {
                Id = id,
                TypeCode = TypeCode,
                Number = null,
                DateUtc = new DateTime(2026, 2, 22, 0, 0, 0, DateTimeKind.Utc),
                Status = DocumentStatus.MarkedForDeletion,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                MarkedForDeletionAtUtc = new DateTime(2026, 2, 22, 10, 0, 0, DateTimeKind.Utc)
            });

        // Act
        var act = () => svc.UpdateDraftAsync(TypeCode, id, new RecordPayload(), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<DocumentMarkedForDeletionException>();
    }

    [Fact]
    public async Task UpdateDraftAsync_WhenLockedDocumentIsNotDraft_ThrowsWorkflowMismatch()
    {
        // Arrange
        var id = Guid.NewGuid();
        var meta = BuildMetaWithHead(TypeCode, "doc_test", [
            Col("document_id", ColumnType.Guid, true),
            Col("display", ColumnType.String, true),
        ]);

        var svc = CreateSut(
            meta,
            lockedDoc: new DocumentRecord
            {
                Id = id,
                TypeCode = TypeCode,
                Number = "N-1",
                DateUtc = new DateTime(2026, 2, 22, 0, 0, 0, DateTimeKind.Utc),
                Status = DocumentStatus.Posted,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                PostedAtUtc = DateTime.UtcNow
            });

        // Act
        var act = () => svc.UpdateDraftAsync(TypeCode, id, new RecordPayload(), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<DocumentWorkflowStateMismatchException>();
    }

    [Fact]
    public async Task DeleteDraftAsync_WhenDocumentDoesNotExist_IsNoOp()
    {
        // Arrange
        var id = Guid.NewGuid();
        var meta = BuildMetaWithHead(TypeCode, "doc_test", [Col("document_id", ColumnType.Guid, true), Col("display", ColumnType.String, true)]);

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
        var relationshipGraph = new Mock<IDocumentRelationshipGraphReadService>(MockBehavior.Strict);
        docs.Setup(x => x.GetForUpdateAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentRecord?)null);

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
        await svc.DeleteDraftAsync(TypeCode, id, CancellationToken.None);
    }

    private static DocumentService CreateSut(
        DocumentTypeMetadata meta,
        DocumentRecord? lockedDoc = null,
        DocumentHeadRow? existingRow = null,
        Action<Mock<IDocumentReader>>? readerSetup = null,
        Action<Mock<IDocumentWriter>>? writerSetup = null)
    {
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
        var relationshipGraph = new Mock<IDocumentRelationshipGraphReadService>(MockBehavior.Strict);
        if (lockedDoc is not null)
        {
            docs.Setup(x => x.GetForUpdateAsync(lockedDoc.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(lockedDoc);

            docs.Setup(x => x.UpdateDraftHeaderAsync(
                    lockedDoc.Id,
                    lockedDoc.Number,
                    lockedDoc.DateUtc,
                    It.IsAny<DateTime>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
        }

        if (existingRow is not null)
        {
            reader.Setup(x => x.GetByIdAsync(It.IsAny<DocumentHeadDescriptor>(), existingRow.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(existingRow);
        }

        // UpdateDraft returns GetById => always return a row when possible.
        reader.Setup(x => x.GetByIdAsync(It.IsAny<DocumentHeadDescriptor>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingRow);

        // GetPage can call both Count and GetPage; keep them unconfigured unless needed.
        readerSetup?.Invoke(reader);

        // Writer is used by UpdateDraft
        writerSetup?.Invoke(writer);

        // Default strict: if test doesn't set them, and call doesn't happen, it's fine.

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

    private static Mock<IDocumentTypeRegistry> CreateRegistryMock(DocumentTypeMetadata meta)
    {
        var reg = new Mock<IDocumentTypeRegistry>(MockBehavior.Strict);
        reg.Setup(x => x.TryGet(TypeCode)).Returns(meta);
        reg.Setup(x => x.GetAll()).Returns([meta]);
        return reg;
    }

    private static DocumentTypeMetadata BuildMetaWithHead(
        string typeCode,
        string headTable,
        IReadOnlyList<DocumentColumnMetadata> columns,
        IReadOnlyList<DocumentListFilterMetadata>? listFilters = null)
        => new(
            TypeCode: typeCode,
            Tables:
            [
                new DocumentTableMetadata(headTable, TableKind.Head, columns)
            ],
            Presentation: new DocumentPresentationMetadata(typeCode),
            Version: new DocumentMetadataVersion(1, "tests"),
            ListFilters: listFilters);

    private static DocumentColumnMetadata Col(string name, ColumnType type, bool required = false)
        => new(name, type, Required: required);
}
