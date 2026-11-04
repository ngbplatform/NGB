using FluentAssertions;
using Moq;
using NGB.Accounting.Documents;
using NGB.Definitions;
using NGB.Metadata.Base;
using NGB.Metadata.Documents.Hybrid;
using NGB.Metadata.Documents.Storage;
using NGB.Persistence.Documents;
using NGB.Persistence.Documents.Universal;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Documents;
using NGB.Runtime.Documents.Derivations;
using NGB.Runtime.Documents.GeneralJournalEntry;
using NGB.Runtime.Documents.Posting;
using NGB.Runtime.Ui;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Documents;

public sealed class DocumentServiceMetadataValidationFullCoverageTests
{
    private const string TypeCode = "test.metadata_validation";

    [Fact]
    public async Task Specialized_journal_metadata_does_not_require_a_generic_storage_descriptor()
    {
        var builder = new DefinitionsBuilder();
        new GeneralJournalEntryDefinitionsContributor().Contribute(builder);
        var definitions = builder.Build();
        Assert.True(definitions.TryGetDocument(AccountingDocumentTypeCodes.GeneralJournalEntry, out var definition));
        var metadata = definition!.Metadata;
        var registry = new Mock<IDocumentTypeRegistry>(MockBehavior.Strict);
        registry.Setup(x => x.TryGet(metadata.TypeCode)).Returns(metadata);
        registry.Setup(x => x.GetAll()).Returns([metadata]);
        var sut = CreateSut(registry.Object);

        var actual = await sut.GetTypeMetadataAsync(metadata.TypeCode, default);
        var all = await sut.GetAllMetadataAsync(default);

        actual.Should().BeEquivalentTo(Assert.Single(all));
        Assert.Equal(metadata.TypeCode, actual.DocumentType);
        Assert.Equal(2, actual.Parts!.Count);

        Func<Task> genericRead = () => sut.GetByIdAsync(metadata.TypeCode, Guid.NewGuid(), default);
        await genericRead.Should().ThrowAsync<NgbConfigurationViolationException>().WithMessage("*display column*");
    }

    [Fact]
    public async Task Optional_presentation_and_explicit_empty_filters_preserve_generic_reads()
    {
        var metadata = new DocumentTypeMetadata(
            TypeCode,
            [
                new DocumentTableMetadata(
                    "doc_test",
                    TableKind.Head,
                    [
                        new DocumentColumnMetadata("document_id", ColumnType.Guid, Required: true),
                        new DocumentColumnMetadata("display", ColumnType.String, Required: true)
                    ])
            ],
            ListFilters: []);
        var sut = CreateSut(Registry(metadata));

        var actual = await sut.GetTypeMetadataAsync(TypeCode, default);
        Assert.Equal(TypeCode, actual.DocumentType);

        Func<Task> missingRecord = () => sut.GetByIdAsync(TypeCode, Guid.NewGuid(), default);
        await missingRecord.Should().ThrowAsync<NGB.Core.Documents.Exceptions.DocumentNotFoundException>();
    }

    [Fact]
    public async Task Metadata_lookup_rejects_blank_unknown_and_missing_head_models()
    {
        var unknownRegistry = new Mock<IDocumentTypeRegistry>(MockBehavior.Strict);
        unknownRegistry.Setup(x => x.TryGet("unknown")).Returns((DocumentTypeMetadata?)null);
        var unknownService = CreateSut(unknownRegistry.Object);

        Func<Task> blank = async () => await unknownService.GetTypeMetadataAsync(" ", default);
        Func<Task> unknown = async () => await unknownService.GetTypeMetadataAsync("unknown", default);
        await blank.Should().ThrowAsync<NgbArgumentRequiredException>();
        await unknown.Should().ThrowAsync<NGB.Core.Documents.Exceptions.DocumentTypeNotFoundException>();

        var partOnly = new DocumentTypeMetadata(
            TypeCode,
            [
                new DocumentTableMetadata(
                    "doc_test__lines",
                    TableKind.Part,
                    [new DocumentColumnMetadata("document_id", ColumnType.Guid, Required: true)],
                    PartCode: "lines")
            ],
            new DocumentPresentationMetadata("Part only"),
            new DocumentMetadataVersion(1, "tests"));
        Func<Task> missingHead = async () => await CreateSut(Registry(partOnly))
            .GetTypeMetadataAsync(TypeCode, default);
        await missingHead.Should().ThrowAsync<NgbConfigurationViolationException>()
            .WithMessage("*has no Head table metadata*");
    }

    [Fact]
    public async Task Amount_field_metadata_rejects_missing_and_non_numeric_columns()
    {
        DocumentTypeMetadata Meta(string amountField, params DocumentColumnMetadata[] extraColumns) => new(
            TypeCode,
            [
                new DocumentTableMetadata(
                    "doc_test",
                    TableKind.Head,
                    [
                        new DocumentColumnMetadata("document_id", ColumnType.Guid, Required: true),
                        new DocumentColumnMetadata("display", ColumnType.String, Required: true),
                        .. extraColumns
                    ])
            ],
            new DocumentPresentationMetadata("Metadata validation", AmountField: amountField),
            new DocumentMetadataVersion(1, "tests"));

        Func<Task> missing = async () => await CreateSut(Registry(Meta("missing_amount")))
            .GetTypeMetadataAsync(TypeCode, default);
        Func<Task> unsupported = async () => await CreateSut(Registry(Meta(
                "text_amount",
                new DocumentColumnMetadata("text_amount", ColumnType.String))))
            .GetTypeMetadataAsync(TypeCode, default);

        await missing.Should().ThrowAsync<NgbConfigurationViolationException>()
            .WithMessage("*no such head column exists*");
        await unsupported.Should().ThrowAsync<NgbConfigurationViolationException>()
            .WithMessage("*unsupported type 'String'*");
    }

    private static IDocumentTypeRegistry Registry(DocumentTypeMetadata metadata)
    {
        var registry = new Mock<IDocumentTypeRegistry>(MockBehavior.Strict);
        registry.Setup(x => x.TryGet(TypeCode)).Returns(metadata);
        return registry.Object;
    }

    private static DocumentService CreateSut(IDocumentTypeRegistry registry)
        => new(
            Mock.Of<IUnitOfWork>(),
            Mock.Of<IDocumentRepository>(),
            Mock.Of<IDocumentDraftService>(),
            registry,
            Mock.Of<IDocumentReader>(),
            Mock.Of<IDocumentPartsReader>(),
            Mock.Of<IDocumentPartsWriter>(),
            Mock.Of<IDocumentWriter>(),
            Mock.Of<IDocumentPostingService>(),
            Mock.Of<IDocumentDerivationService>(),
            Mock.Of<IDocumentPostingActionResolver>(),
            Mock.Of<IDocumentRelationshipGraphReadService>(),
            NoOpReferencePayloadEnricher.Instance,
            []);
}
