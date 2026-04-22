using FluentAssertions;
using Moq;
using NGB.Contracts.Metadata;
using NGB.Metadata.Base;
using NGB.Metadata.Documents.Hybrid;
using NGB.Persistence.Documents;
using NGB.Persistence.Documents.Universal;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Documents;
using NGB.Runtime.Documents.Derivations;
using NGB.Runtime.Documents.Posting;
using NGB.Runtime.Ui;
using NGB.Metadata.Documents.Storage;
using Xunit;

namespace NGB.Runtime.Tests.Documents;

public sealed class DocumentService_Metadata_Parts_P0Tests
{
    private const string TypeCode = "test_doc";

    [Fact]
    public async Task GetTypeMetadataAsync_WhenDocumentFieldDeclaresMirroredRelationship_ExposesItInFormMetadata()
    {
        // Arrange
        var meta = new DocumentTypeMetadata(
            TypeCode: TypeCode,
            Tables:
            [
                new DocumentTableMetadata(
                    TableName: "doc_test",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String, Required: true),
                        new(
                            "source_document_id",
                            ColumnType.Guid,
                            Lookup: new DocumentLookupSourceMetadata(["test.source_document"]),
                            MirroredRelationship: new MirroredDocumentRelationshipMetadata("created_from")),
                    ])
            ],
            Presentation: new DocumentPresentationMetadata(TypeCode),
            Version: new DocumentMetadataVersion(1, "tests"));

        var uow = new Mock<IUnitOfWork>(MockBehavior.Strict);
        var docs = new Mock<IDocumentRepository>(MockBehavior.Strict);
        var drafts = new Mock<IDocumentDraftService>(MockBehavior.Strict);
        var reg = new Mock<IDocumentTypeRegistry>(MockBehavior.Strict);
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

        reg.Setup(x => x.TryGet(TypeCode)).Returns(meta);

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
        var dto = await svc.GetTypeMetadataAsync(TypeCode, CancellationToken.None);

        // Assert
        var field = dto.Form!.Sections
            .SelectMany(s => s.Rows)
            .SelectMany(r => r.Fields)
            .Single(f => f.Key == "source_document_id");

        field.MirroredRelationship.Should().NotBeNull();
        field.MirroredRelationship!.RelationshipCode.Should().Be("created_from");
    }

    [Fact]
    public async Task GetTypeMetadataAsync_WhenDocumentHasPartTables_IncludesPartsMetadata()
    {
        // Arrange
        var meta = BuildMetaWithParts(TypeCode);

        var uow = new Mock<IUnitOfWork>(MockBehavior.Strict);
        var docs = new Mock<IDocumentRepository>(MockBehavior.Strict);
        var drafts = new Mock<IDocumentDraftService>(MockBehavior.Strict);
        var reg = new Mock<IDocumentTypeRegistry>(MockBehavior.Strict);
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

        reg.Setup(x => x.TryGet(TypeCode)).Returns(meta);

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
        var dto = await svc.GetTypeMetadataAsync(TypeCode, CancellationToken.None);

        // Assert
        dto.Parts.Should().NotBeNull();
        dto.Parts!.Should().HaveCount(1);

        var part = dto.Parts![0];
        part.PartCode.Should().Be("items");
        part.Title.Should().Be("Items");

        part.List.Columns.Select(c => c.Key).Should().BeEquivalentTo(
            ["line_no", "amount", "memo"],
            options => options.WithStrictOrdering());

        part.List.Columns.Single(c => c.Key == "line_no").DataType.Should().Be(DataType.Int32);
        part.List.Columns.Single(c => c.Key == "amount").DataType.Should().Be(DataType.Decimal);
        part.List.Columns.Single(c => c.Key == "memo").DataType.Should().Be(DataType.String);
    }

    [Fact]
    public async Task GetTypeMetadataAsync_WhenListFiltersConfigured_ProjectsLookupMultiAndOptionsMetadata()
    {
        // Arrange
        var meta = new DocumentTypeMetadata(
            TypeCode: TypeCode,
            Tables:
            [
                new DocumentTableMetadata(
                    TableName: "doc_test",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String, Required: true),
                        new("lease_id", ColumnType.Guid, Lookup: new DocumentLookupSourceMetadata(["pm.lease"])),
                        new("priority", ColumnType.String)
                    ])
            ],
            Presentation: new DocumentPresentationMetadata(TypeCode),
            Version: new DocumentMetadataVersion(1, "tests"),
            ListFilters:
            [
                new DocumentListFilterMetadata(
                    Key: "lease_id",
                    Label: "Lease",
                    Type: ColumnType.Guid,
                    IsMulti: true,
                    Lookup: new DocumentLookupSourceMetadata(["pm.lease"])),
                new DocumentListFilterMetadata(
                    Key: "priority",
                    Label: "Priority",
                    Type: ColumnType.String,
                    Options:
                    [
                        new DocumentListFilterOptionMetadata("high", "High"),
                        new DocumentListFilterOptionMetadata("normal", "Normal")
                    ],
                    Description: "Choose one priority")
            ]);

        var uow = new Mock<IUnitOfWork>(MockBehavior.Strict);
        var docs = new Mock<IDocumentRepository>(MockBehavior.Strict);
        var drafts = new Mock<IDocumentDraftService>(MockBehavior.Strict);
        var reg = new Mock<IDocumentTypeRegistry>(MockBehavior.Strict);
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

        reg.Setup(x => x.TryGet(TypeCode)).Returns(meta);

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
        var dto = await svc.GetTypeMetadataAsync(TypeCode, CancellationToken.None);

        // Assert
        dto.List!.Filters.Should().NotBeNull();
        dto.List!.Filters.Should().HaveCount(2);

        var leaseFilter = dto.List!.Filters!.Single(x => x.Key == "lease_id");
        leaseFilter.Label.Should().Be("Lease");
        leaseFilter.IsMulti.Should().BeTrue();
        leaseFilter.Lookup.Should().BeOfType<DocumentLookupSourceDto>()
            .Which.DocumentTypes.Should().Equal("pm.lease");

        var priorityFilter = dto.List.Filters.Single(x => x.Key == "priority");
        priorityFilter.Options.Should().BeEquivalentTo(
            [
                new ListFilterOptionDto("high", "High"),
                new ListFilterOptionDto("normal", "Normal")
            ],
            options => options.WithStrictOrdering());
        priorityFilter.Description.Should().Be("Choose one priority");
    }

    [Fact]
    public async Task GetTypeMetadataAsync_WhenPresentationDefinesAmountField_ProjectsIt_AndPrefersItInListMetadata()
    {
        // Arrange
        var meta = new DocumentTypeMetadata(
            TypeCode: TypeCode,
            Tables:
            [
                new DocumentTableMetadata(
                    TableName: "doc_test",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String, Required: true),
                        new("lease_id", ColumnType.Guid),
                        new("due_on_utc", ColumnType.Date),
                        new("status_code", ColumnType.String),
                        new("memo", ColumnType.String),
                        new("reference_no", ColumnType.String),
                        new("total_due", ColumnType.Decimal)
                    ])
            ],
            Presentation: new DocumentPresentationMetadata(TypeCode, AmountField: "total_due"),
            Version: new DocumentMetadataVersion(1, "tests"));

        var uow = new Mock<IUnitOfWork>(MockBehavior.Strict);
        var docs = new Mock<IDocumentRepository>(MockBehavior.Strict);
        var drafts = new Mock<IDocumentDraftService>(MockBehavior.Strict);
        var reg = new Mock<IDocumentTypeRegistry>(MockBehavior.Strict);
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

        reg.Setup(x => x.TryGet(TypeCode)).Returns(meta);

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
        var dto = await svc.GetTypeMetadataAsync(TypeCode, CancellationToken.None);

        // Assert
        dto.Presentation.Should().NotBeNull();
        dto.Presentation!.AmountField.Should().Be("total_due");
        dto.List!.Columns.Should().Contain(c => c.Key == "total_due");
    }

    private static DocumentTypeMetadata BuildMetaWithParts(string typeCode)
        => new(
            TypeCode: typeCode,
            Tables:
            [
                new DocumentTableMetadata(
                    TableName: "doc_test",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String, Required: true),
                    ]),
                new DocumentTableMetadata(
                    TableName: "doc_test__items_storage",
                    Kind: TableKind.Part,
                    PartCode: "items",
                    Columns:
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("line_no", ColumnType.Int32, Required: true),
                        new("amount", ColumnType.Decimal, Required: true),
                        new("memo", ColumnType.String),
                    ])
            ],
            Presentation: new DocumentPresentationMetadata(typeCode),
            Version: new DocumentMetadataVersion(1, "tests"));
}
