using FluentAssertions;
using Moq;
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

public sealed class DocumentServiceLookupFullCoverageTests
{
    private const string TypeCode = "test.lookup";

    [Fact]
    public async Task Lookup_apis_validate_null_and_return_early_for_limits_empty_inputs_and_blank_type_sets()
    {
        var sut = CreateSut(Mock.Of<IDocumentTypeRegistry>(), Mock.Of<IDocumentReader>());
        var id = Guid.NewGuid();

        Func<Task> nullLookupTypes = async () => await sut.LookupAcrossTypesAsync(
            null!, null, 1, false, default);
        Func<Task> nullReadTypes = async () => await sut.GetByIdsAcrossTypesAsync(
            null!, [id], default);
        Func<Task> nullIds = async () => await sut.GetByIdsAcrossTypesAsync(
            [TypeCode], null!, default);
        await nullLookupTypes.Should().ThrowAsync<NgbArgumentRequiredException>();
        await nullReadTypes.Should().ThrowAsync<NgbArgumentRequiredException>();
        await nullIds.Should().ThrowAsync<NgbArgumentRequiredException>();

        (await sut.LookupAcrossTypesAsync([TypeCode], null, 0, false, default)).Should().BeEmpty();
        (await sut.LookupAcrossTypesAsync([], null, 1, false, default)).Should().BeEmpty();
        (await sut.LookupAcrossTypesAsync([" ", "\t"], null, 1, false, default)).Should().BeEmpty();
        (await sut.GetByIdsAcrossTypesAsync([], [id], default)).Should().BeEmpty();
        (await sut.GetByIdsAcrossTypesAsync([TypeCode], [], default)).Should().BeEmpty();
        (await sut.GetByIdsAcrossTypesAsync([" ", "\t"], [id], default)).Should().BeEmpty();
    }

    [Fact]
    public async Task Lookup_apis_deduplicate_type_codes_delegate_once_and_map_all_rows()
    {
        var meta = new DocumentTypeMetadata(
            TypeCode,
            [
                new DocumentTableMetadata(
                    "doc_test_lookup",
                    TableKind.Head,
                    [
                        new DocumentColumnMetadata("document_id", ColumnType.Guid, Required: true),
                        new DocumentColumnMetadata("display", ColumnType.String, Required: true)
                    ])
            ],
            new DocumentPresentationMetadata("Test lookup"),
            new DocumentMetadataVersion(1, "tests"));
        var registry = new Mock<IDocumentTypeRegistry>(MockBehavior.Strict);
        registry.Setup(x => x.TryGet(TypeCode)).Returns(meta);

        var draftId = Guid.NewGuid();
        var postedId = Guid.NewGuid();
        var rows = new DocumentLookupRow[]
        {
            new(draftId, TypeCode, DocumentStatus.Draft, false, "Draft row", null),
            new(postedId, TypeCode, DocumentStatus.Posted, false, "Posted row", "P-42"),
            new(Guid.NewGuid(), TypeCode, DocumentStatus.MarkedForDeletion, true, null, "D-1")
        };
        IReadOnlyList<DocumentHeadDescriptor>? lookupHeads = null;
        IReadOnlyList<DocumentHeadDescriptor>? readHeads = null;
        var reader = new Mock<IDocumentReader>(MockBehavior.Strict);
        reader.Setup(x => x.LookupAcrossTypesAsync(
                It.IsAny<IReadOnlyList<DocumentHeadDescriptor>>(),
                "needle",
                5,
                true,
                It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<DocumentHeadDescriptor>, string?, int, bool, CancellationToken>(
                (heads, _, _, _, _) => lookupHeads = heads)
            .ReturnsAsync(rows);
        reader.Setup(x => x.GetByIdsAcrossTypesAsync(
                It.IsAny<IReadOnlyList<DocumentHeadDescriptor>>(),
                It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<DocumentHeadDescriptor>, IReadOnlyList<Guid>, CancellationToken>(
                (heads, _, _) => readHeads = heads)
            .ReturnsAsync(rows);

        var sut = CreateSut(registry.Object, reader.Object);
        var documentTypes = new[] { " ", TypeCode, TypeCode.ToUpperInvariant(), "\t" };

        var lookup = await sut.LookupAcrossTypesAsync(documentTypes, "needle", 5, true, default);
        var byIds = await sut.GetByIdsAcrossTypesAsync(documentTypes, [draftId, postedId], default);

        lookupHeads.Should().ContainSingle(x => x.TypeCode == TypeCode);
        readHeads.Should().ContainSingle(x => x.TypeCode == TypeCode);
        lookup.Should().BeEquivalentTo(byIds, options => options.WithStrictOrdering());
        lookup.Should().HaveCount(3);
        lookup[0].Status.Should().Be(NGB.Contracts.Metadata.DocumentStatus.Draft);
        lookup[1].Status.Should().Be(NGB.Contracts.Metadata.DocumentStatus.Posted);
        lookup[1].Number.Should().Be("P-42");
        lookup[2].Status.Should().Be(NGB.Contracts.Metadata.DocumentStatus.MarkedForDeletion);
        lookup[2].IsMarkedForDeletion.Should().BeTrue();
        lookup[2].Display.Should().BeNull();
        registry.Verify(x => x.TryGet(TypeCode), Times.Exactly(2));
    }

    private static DocumentService CreateSut(IDocumentTypeRegistry registry, IDocumentReader reader)
        => new(
            Mock.Of<IUnitOfWork>(),
            Mock.Of<IDocumentRepository>(),
            Mock.Of<IDocumentDraftService>(),
            registry,
            reader,
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
