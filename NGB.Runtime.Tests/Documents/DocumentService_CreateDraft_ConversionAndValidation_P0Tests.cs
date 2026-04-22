using System.Text.Json;
using FluentAssertions;
using Moq;
using NGB.Contracts.Common;
using NGB.Core.Documents;
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
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Documents;

public sealed class DocumentService_CreateDraft_ConversionAndValidation_P0Tests
{
    private const string TypeCode = "test_doc";

    [Fact]
    public async Task CreateDraftAsync_Converts_AllSupportedScalarTypes_AndPassesThemToWriter()
    {
        // Arrange
        var meta = BuildDocMeta(
            TypeCode,
            headTable: "doc_test_doc",
            columns:
            [
                Col("document_id", ColumnType.Guid, required: true),
                Col("display", ColumnType.String, required: true),
                Col("s", ColumnType.String),
                Col("i32", ColumnType.Int32),
                Col("i64", ColumnType.Int64),
                Col("dec", ColumnType.Decimal),
                Col("b", ColumnType.Boolean),
                Col("g", ColumnType.Guid),
                Col("d", ColumnType.Date),
                Col("ts", ColumnType.DateTimeUtc),
                Col("js", ColumnType.Json),
            ]);

        var newId = Guid.NewGuid();

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
        drafts
            .Setup(x => x.CreateDraftAsync(
                TypeCode,
                It.IsAny<string?>(),
                It.IsAny<DateTime>(),
                manageTransaction: false,
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(newId);

        IReadOnlyList<DocumentHeadValue>? captured = null;
        writer
            .Setup(x => x.UpsertHeadAsync(
                It.Is<DocumentHeadDescriptor>(h =>
                    h.TypeCode == TypeCode &&
                    h.HeadTableName == "doc_test_doc" &&
                    string.Equals(h.DisplayColumn, "display", StringComparison.OrdinalIgnoreCase)),
                newId,
                It.IsAny<IReadOnlyList<DocumentHeadValue>>(),
                It.IsAny<CancellationToken>()))
            .Callback<DocumentHeadDescriptor, Guid, IReadOnlyList<DocumentHeadValue>, CancellationToken>((_, _, v, _) => captured = v)
            .Returns(Task.CompletedTask);

        // CreateDraft returns GetById => arrange reader to return a row.
        reader
            .Setup(x => x.GetByIdAsync(
                It.IsAny<DocumentHeadDescriptor>(),
                newId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentHeadRow(
                newId,
                DocumentStatus.Draft,
                IsMarkedForDeletion: false,
                Display: "d",
                Fields: new Dictionary<string, object?>
                {
                    ["display"] = "d",
                    ["s"] = "abc",
                    ["i32"] = 123,
                    ["i64"] = 456L,
                    ["dec"] = 12.34m,
                    ["b"] = true,
                    ["g"] = newId,
                    ["d"] = new DateOnly(2026, 2, 22),
                    ["ts"] = new DateTime(2026, 2, 22, 10, 11, 12, DateTimeKind.Utc),
                    ["js"] = "{\"a\":1}",
                }));

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

        var payload = new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["display"] = JsonSerializer.SerializeToElement("Lease #1"),
            ["s"] = JsonSerializer.SerializeToElement(123), // non-string => coerces via ToString
            ["i32"] = JsonSerializer.SerializeToElement("42"),
            ["i64"] = JsonSerializer.SerializeToElement(456L),
            ["dec"] = JsonSerializer.SerializeToElement("12.34"),
            ["b"] = JsonSerializer.SerializeToElement("true"),
            ["g"] = JsonSerializer.SerializeToElement("00000000-0000-0000-0000-000000000001"),
            ["d"] = JsonSerializer.SerializeToElement("2026-02-22"),
            ["ts"] = JsonSerializer.SerializeToElement("2026-02-22T10:11:12Z"),
            ["js"] = JsonSerializer.SerializeToElement(new { a = 1, b = "x" }),
        });

        // Act
        var created = await svc.CreateDraftAsync(TypeCode, payload, CancellationToken.None);

        // Assert
        created.Id.Should().Be(newId);
        captured.Should().NotBeNull();

        var byName = captured!.ToDictionary(x => x.ColumnName, StringComparer.OrdinalIgnoreCase);
        byName["display"].Value.Should().Be("Lease #1");
        byName["s"].Value.Should().Be("123");
        byName["i32"].Value.Should().Be(42);
        byName["i64"].Value.Should().Be(456L);
        byName["dec"].Value.Should().Be(12.34m);
        byName["b"].Value.Should().Be(true);
        byName["g"].Value.Should().Be(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        byName["d"].Value.Should().Be(new DateOnly(2026, 2, 22));
        byName["ts"].Value.Should().Be(new DateTime(2026, 2, 22, 10, 11, 12, DateTimeKind.Utc));

        // ColumnType.Json: stored as raw JSON string.
        byName["js"].Value.Should().BeOfType<string>();
        ((string)byName["js"].Value!).Should().Contain("\"a\"");

        // And: UoW transaction wrapper invoked.
        uow.Verify(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateDraftAsync_WhenPartsProvidedButDocumentHasNoParts_Throws()
    {
        // Arrange
        var svc = CreateSutForCreateDraft(BuildMinimalMetaWithDisplay());

        var payload = new RecordPayload(
            Fields: new Dictionary<string, JsonElement>
            {
                ["display"] = JsonSerializer.SerializeToElement("x"),
            },
            Parts: new Dictionary<string, RecordPartPayload>
            {
                ["lines"] = new RecordPartPayload([
                    new Dictionary<string, JsonElement> { ["a"] = JsonSerializer.SerializeToElement(1) }
                ])
            });

        // Act
        var act = () => svc.CreateDraftAsync(TypeCode, payload, CancellationToken.None);

        // Assert
        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("payload");
        ex.Which.Message.Should().Be("This document does not support tabular parts.");
    }

    [Fact]
    public async Task CreateDraftAsync_WhenUnknownPartProvided_ThrowsFriendlyMessage()
    {
        // Arrange
        var svc = CreateSutForCreateDraft(BuildDocMetaWithLinesPart());

        var payload = new RecordPayload(
            Fields: new Dictionary<string, JsonElement>
            {
                ["display"] = JsonSerializer.SerializeToElement("x"),
            },
            Parts: new Dictionary<string, RecordPartPayload>
            {
                ["unknown_lines"] = new RecordPartPayload([
                    new Dictionary<string, JsonElement> { ["line_no"] = JsonSerializer.SerializeToElement(1) }
                ])
            });

        // Act
        var act = () => svc.CreateDraftAsync(TypeCode, payload, CancellationToken.None);

        // Assert
        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("unknown_lines");
        ex.Which.Message.Should().Be("Part 'Unknown Lines' is not available on this form.");
    }

    [Fact]
    public async Task CreateDraftAsync_WhenPartRowContainsDocumentId_ThrowsFriendlyMessage()
    {
        // Arrange
        var svc = CreateSutForCreateDraft(BuildDocMetaWithLinesPart());

        var payload = new RecordPayload(
            Fields: new Dictionary<string, JsonElement>
            {
                ["display"] = JsonSerializer.SerializeToElement("x"),
            },
            Parts: new Dictionary<string, RecordPartPayload>
            {
                ["lines"] = new RecordPartPayload([
                    new Dictionary<string, JsonElement>
                    {
                        ["document_id"] = JsonSerializer.SerializeToElement(Guid.Empty),
                        ["line_no"] = JsonSerializer.SerializeToElement(1),
                        ["amount"] = JsonSerializer.SerializeToElement(10)
                    }
                ])
            });

        // Act
        var act = () => svc.CreateDraftAsync(TypeCode, payload, CancellationToken.None);

        // Assert
        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("lines[0].document_id");
        ex.Which.Message.Should().Be("Document Id is managed automatically and cannot be set in Lines row 1.");
    }

    [Fact]
    public async Task CreateDraftAsync_WhenPartRowContainsUnknownField_ThrowsFriendlyMessage()
    {
        // Arrange
        var svc = CreateSutForCreateDraft(BuildDocMetaWithLinesPart());

        var payload = new RecordPayload(
            Fields: new Dictionary<string, JsonElement>
            {
                ["display"] = JsonSerializer.SerializeToElement("x"),
            },
            Parts: new Dictionary<string, RecordPartPayload>
            {
                ["lines"] = new RecordPartPayload([
                    new Dictionary<string, JsonElement>
                    {
                        ["line_no"] = JsonSerializer.SerializeToElement(1),
                        ["amount"] = JsonSerializer.SerializeToElement(10),
                        ["bogus_code"] = JsonSerializer.SerializeToElement("x")
                    }
                ])
            });

        // Act
        var act = () => svc.CreateDraftAsync(TypeCode, payload, CancellationToken.None);

        // Assert
        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("lines[0].bogus_code");
        ex.Which.Message.Should().Be("Field 'Bogus Code' is not available in Lines row 1.");
    }

    [Fact]
    public async Task CreateDraftAsync_WhenPartRowIsMissingRequiredField_ThrowsFriendlyMessage()
    {
        // Arrange
        var svc = CreateSutForCreateDraft(BuildDocMetaWithLinesPart());

        var payload = new RecordPayload(
            Fields: new Dictionary<string, JsonElement>
            {
                ["display"] = JsonSerializer.SerializeToElement("x"),
            },
            Parts: new Dictionary<string, RecordPartPayload>
            {
                ["lines"] = new RecordPartPayload([
                    new Dictionary<string, JsonElement>
                    {
                        ["line_no"] = JsonSerializer.SerializeToElement(1)
                    }
                ])
            });

        // Act
        var act = () => svc.CreateDraftAsync(TypeCode, payload, CancellationToken.None);

        // Assert
        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("lines[0].amount");
        ex.Which.Message.Should().Be("Amount is required in Lines row 1.");
    }

    [Fact]
    public async Task CreateDraftAsync_WhenUnknownFieldProvided_Throws()
    {
        // Arrange
        var svc = CreateSutForCreateDraft(BuildMinimalMetaWithDisplay());
        var payload = new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["display"] = JsonSerializer.SerializeToElement("x"),
            ["unknown"] = JsonSerializer.SerializeToElement("y"),
        });

        // Act
        var act = () => svc.CreateDraftAsync(TypeCode, payload, CancellationToken.None);

        // Assert
        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("payload");
        ex.Which.Message.Should().Be("Field 'Unknown' is not available on this form.");
    }

    [Fact]
    public async Task CreateDraftAsync_WhenMissingRequiredField_Throws()
    {
        // Arrange
        var meta = BuildDocMeta(
            TypeCode,
            headTable: "doc_test_doc",
            columns:
            [
                Col("document_id", ColumnType.Guid, required: true),
                Col("display", ColumnType.String, required: true),
                Col("req", ColumnType.Int32, required: true),
            ]);

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

        var payload = new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["display"] = JsonSerializer.SerializeToElement("x"),
            // missing req
        });

        // Act
        var act = () => svc.CreateDraftAsync(TypeCode, payload, CancellationToken.None);

        // Assert
        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("payload.Fields.req");
        ex.Which.Message.Should().Be("Req is required.");
    }

    [Theory]
    [InlineData("12,34")]
    [InlineData("1e3")]
    public async Task CreateDraftAsync_WhenDecimalIsNotInvariantStrict_Throws(string raw)
    {
        // Arrange
        var meta = BuildDocMeta(
            TypeCode,
            headTable: "doc_test_doc",
            columns:
            [
                Col("document_id", ColumnType.Guid, required: true),
                Col("display", ColumnType.String, required: true),
                Col("dec", ColumnType.Decimal, required: true),
            ]);

        var svc = CreateSutForCreateDraft(meta);
        var payload = new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["display"] = JsonSerializer.SerializeToElement("x"),
            ["dec"] = JsonSerializer.SerializeToElement(raw),
        });

        // Act
        var act = () => svc.CreateDraftAsync(TypeCode, payload, CancellationToken.None);

        // Assert
        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("payload.Fields.dec");
        ex.Which.Message.Should().Be("Enter a valid number for Dec.");
    }

    [Fact]
    public async Task CreateDraftAsync_WhenDateTimeIsNotUtc_Throws()
    {
        // Arrange
        var meta = BuildDocMeta(
            TypeCode,
            headTable: "doc_test_doc",
            columns:
            [
                Col("document_id", ColumnType.Guid, required: true),
                Col("display", ColumnType.String, required: true),
                Col("ts", ColumnType.DateTimeUtc, required: true),
            ]);

        var svc = CreateSutForCreateDraft(meta);
        var payload = new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["display"] = JsonSerializer.SerializeToElement("x"),
            // No 'Z' => RoundtripKind produces Unspecified.
            ["ts"] = JsonSerializer.SerializeToElement("2026-02-22T10:11:12"),
        });

        // Act
        var act = () => svc.CreateDraftAsync(TypeCode, payload, CancellationToken.None);

        // Assert
        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("payload.Fields.ts");
        ex.Which.Message.Should().Be("Enter a valid date and time for Ts.");
    }

    private static DocumentService CreateSutForCreateDraft(DocumentTypeMetadata meta)
    {
        var newId = Guid.NewGuid();

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
        var relationshipGraph = new Mock<IDocumentRelationshipGraphReadService>(MockBehavior.Strict);
        var opregPostingActionResolver = new Mock<IDocumentOperationalRegisterPostingActionResolver>(MockBehavior.Strict);
        var refregPostingActionResolver = new Mock<IDocumentReferenceRegisterPostingActionResolver>(MockBehavior.Strict);
        drafts
            .Setup(x => x.CreateDraftAsync(
                TypeCode,
                It.IsAny<string?>(),
                It.IsAny<DateTime>(),
                manageTransaction: false,
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(newId);

        writer
            .Setup(x => x.UpsertHeadAsync(
                It.IsAny<DocumentHeadDescriptor>(),
                newId,
                It.IsAny<IReadOnlyList<DocumentHeadValue>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // CreateDraft returns GetById => return a stub row
        reader
            .Setup(x => x.GetByIdAsync(It.IsAny<DocumentHeadDescriptor>(), newId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentHeadRow(newId, DocumentStatus.Draft, false, "d", new Dictionary<string, object?> { ["display"] = "d" }));

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

    private static DocumentTypeMetadata BuildMinimalMetaWithDisplay()
        => BuildDocMeta(
            TypeCode,
            headTable: "doc_test_doc",
            columns:
            [
                Col("document_id", ColumnType.Guid, required: true),
                Col("display", ColumnType.String, required: true),
            ]);

    private static DocumentTypeMetadata BuildDocMetaWithLinesPart()
        => new(
            TypeCode: TypeCode,
            Tables:
            [
                new DocumentTableMetadata(
                    TableName: "doc_test_doc",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        Col("document_id", ColumnType.Guid, required: true),
                        Col("display", ColumnType.String, required: true),
                    ]),
                new DocumentTableMetadata(
                    TableName: "doc_test_doc__line_storage",
                    Kind: TableKind.Part,
                    PartCode: "lines",
                    Columns:
                    [
                        Col("document_id", ColumnType.Guid, required: true),
                        Col("line_no", ColumnType.Int32, required: true),
                        Col("amount", ColumnType.Decimal, required: true),
                    ])
            ],
            Presentation: new DocumentPresentationMetadata(TypeCode),
            Version: new DocumentMetadataVersion(1, "tests"));

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

    private static DocumentTypeMetadata BuildDocMeta(
        string typeCode,
        string headTable,
        IReadOnlyList<DocumentColumnMetadata> columns)
        => new(
            TypeCode: typeCode,
            Tables:
            [
                new DocumentTableMetadata(
                    TableName: headTable,
                    Kind: TableKind.Head,
                    Columns: columns)
            ],
            Presentation: new DocumentPresentationMetadata(typeCode),
            Version: new DocumentMetadataVersion(1, "tests"));

    private static DocumentColumnMetadata Col(string name, ColumnType type, bool required = false)
        => new(name, type, Required: required);
}
