using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Definitions;
using NGB.Definitions.Catalogs;
using NGB.Definitions.Documents;
using NGB.Definitions.Documents.Relationships;
using NGB.Metadata.Base;
using NGB.Metadata.Documents.Hybrid;
using NGB.Runtime.Definitions.Validation;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

public sealed class DefinitionsValidationService_MirroredRelationships_P0Tests
{
    [Fact]
    public void ValidateOrThrow_WhenMirroredRelationshipFieldShapeIsInvalid_ReportsFieldErrors()
    {
        var metadata = new DocumentTypeMetadata(
            TypeCode: "doc.source",
            Tables:
            [
                new DocumentTableMetadata(
                    TableName: "doc_source",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new DocumentColumnMetadata(
                            ColumnName: "relation_as_text",
                            Type: ColumnType.String,
                            Lookup: new DocumentLookupSourceMetadata(["doc.target"]),
                            MirroredRelationship: new MirroredDocumentRelationshipMetadata("created_from")),
                        new DocumentColumnMetadata(
                            ColumnName: "relation_catalog_id",
                            Type: ColumnType.Guid,
                            Lookup: new CatalogLookupSourceMetadata("cat.test"),
                            MirroredRelationship: new MirroredDocumentRelationshipMetadata("created_from")),
                        new DocumentColumnMetadata(
                            ColumnName: "relation_missing_target_id",
                            Type: ColumnType.Guid,
                            Lookup: new DocumentLookupSourceMetadata(["doc.missing"]),
                            MirroredRelationship: new MirroredDocumentRelationshipMetadata("created_from"))
                    ]),
                new DocumentTableMetadata(
                    TableName: "doc_source__lines",
                    Kind: TableKind.Part,
                    PartCode: "lines",
                    Columns:
                    [
                        new DocumentColumnMetadata(
                            ColumnName: "parent_document_id",
                            Type: ColumnType.Guid,
                            Lookup: new DocumentLookupSourceMetadata(["doc.target"]),
                            MirroredRelationship: new MirroredDocumentRelationshipMetadata("created_from"))
                    ])
            ],
            Presentation: new DocumentPresentationMetadata("Source"));

        var registry = BuildRegistry(
            documents:
            [
                new DocumentTypeDefinition("doc.source", metadata),
                new DocumentTypeDefinition("doc.target", NewDocMetadata("doc.target"))
            ],
            relationships:
            [
                new DocumentRelationshipTypeDefinition(
                    Code: "created_from",
                    Name: "Created From",
                    IsBidirectional: false,
                    Cardinality: DocumentRelationshipCardinality.ManyToOne,
                    AllowedFromTypeCodes: null,
                    AllowedToTypeCodes: null)
            ]);

        var validator = CreateInternalValidator(registry, new AlwaysTrueIsService());

        var act = () => validator.ValidateOrThrow();

        var ex = act.Should().Throw<DefinitionsValidationException>().Which;
        ex.Errors.Should().HaveCount(4);
        ex.Errors.Should().Contain(e => e.Contains("doc_source.relation_as_text") && e.Contains("ColumnType.Guid"));
        ex.Errors.Should().Contain(e => e.Contains("doc_source.relation_catalog_id") && e.Contains("document lookup field"));
        ex.Errors.Should().Contain(e => e.Contains("doc_source.relation_missing_target_id") && e.Contains("unknown target document type 'doc.missing'"));
        ex.Errors.Should().Contain(e => e.Contains("doc_source__lines.parent_document_id") && e.Contains("head-table column"));
    }

    [Fact]
    public void ValidateOrThrow_WhenMirroredRelationshipCodeIsBlankOrUntrimmed_ReportsConfigurationError()
    {
        var registry = BuildRegistry(
            documents:
            [
                new DocumentTypeDefinition(
                    "doc.source",
                    new DocumentTypeMetadata(
                        TypeCode: "doc.source",
                        Tables:
                        [
                            new DocumentTableMetadata(
                                TableName: "doc_source",
                                Kind: TableKind.Head,
                                Columns:
                                [
                                    new DocumentColumnMetadata(
                                        ColumnName: "related_document_id",
                                        Type: ColumnType.Guid,
                                        Lookup: new DocumentLookupSourceMetadata(["doc.target"]),
                                        MirroredRelationship: new MirroredDocumentRelationshipMetadata("  created_from  "))
                                ])
                        ],
                        Presentation: new DocumentPresentationMetadata("Source"))),
                new DocumentTypeDefinition("doc.target", NewDocMetadata("doc.target"))
            ],
            relationships:
            [
                new DocumentRelationshipTypeDefinition(
                    Code: "created_from",
                    Name: "Created From",
                    IsBidirectional: false,
                    Cardinality: DocumentRelationshipCardinality.ManyToOne,
                    AllowedFromTypeCodes: null,
                    AllowedToTypeCodes: null)
            ]);

        var validator = CreateInternalValidator(registry, new AlwaysTrueIsService());

        var act = () => validator.ValidateOrThrow();

        var ex = act.Should().Throw<DefinitionsValidationException>().Which;
        ex.Errors.Should().ContainSingle(e => e.Contains("doc_source.related_document_id") && e.Contains("non-empty trimmed relationship code"));
    }

    [Fact]
    public void ValidateOrThrow_WhenMirroredRelationshipUsesUnknownOrIncompatibleRelationshipType_ReportsCompatibilityErrors()
    {
        var unknownRelRegistry = BuildRegistry(
            documents:
            [
                new DocumentTypeDefinition(
                    "doc.source",
                    NewDocMetadata(
                        "doc.source",
                        new DocumentColumnMetadata(
                            ColumnName: "related_document_id",
                            Type: ColumnType.Guid,
                            Lookup: new DocumentLookupSourceMetadata(["doc.target"]),
                            MirroredRelationship: new MirroredDocumentRelationshipMetadata("unknown.rel")))),
                new DocumentTypeDefinition("doc.target", NewDocMetadata("doc.target"))
            ]);

        var unknownRelValidator = CreateInternalValidator(unknownRelRegistry, new AlwaysTrueIsService());
        var unknownEx = Assert.Throws<DefinitionsValidationException>(() => unknownRelValidator.ValidateOrThrow());
        unknownEx.Errors.Should().ContainSingle(e => e.Contains("unknown relationship type 'unknown.rel'"));

        var incompatibleRegistry = BuildRegistry(
            documents:
            [
                new DocumentTypeDefinition(
                    "doc.source",
                    NewDocMetadata(
                        "doc.source",
                        new DocumentColumnMetadata(
                            ColumnName: "related_document_id",
                            Type: ColumnType.Guid,
                            Lookup: new DocumentLookupSourceMetadata(["doc.target"]),
                            MirroredRelationship: new MirroredDocumentRelationshipMetadata("created_from")))),
                new DocumentTypeDefinition("doc.target", NewDocMetadata("doc.target")),
                new DocumentTypeDefinition("doc.allowed_source", NewDocMetadata("doc.allowed_source")),
                new DocumentTypeDefinition("doc.allowed_target", NewDocMetadata("doc.allowed_target"))
            ],
            relationships:
            [
                new DocumentRelationshipTypeDefinition(
                    Code: "created_from",
                    Name: "Created From",
                    IsBidirectional: false,
                    Cardinality: DocumentRelationshipCardinality.ManyToOne,
                    AllowedFromTypeCodes: ["doc.allowed_source"],
                    AllowedToTypeCodes: ["doc.allowed_target"])
            ]);

        var incompatibleValidator = CreateInternalValidator(incompatibleRegistry, new AlwaysTrueIsService());

        var act = () => incompatibleValidator.ValidateOrThrow();

        var incompatibleEx = act.Should().Throw<DefinitionsValidationException>().Which;
        incompatibleEx.Errors.Should().HaveCount(2);
        incompatibleEx.Errors.Should().Contain(e => e.Contains("does not allow from-document type 'doc.source'"));
        incompatibleEx.Errors.Should().Contain(e => e.Contains("does not allow target document type 'doc.target'"));
    }

    [Fact]
    public void ValidateOrThrow_WhenMirroredRelationshipFieldIsValid_DoesNotAddErrors()
    {
        var registry = BuildRegistry(
            documents:
            [
                new DocumentTypeDefinition(
                    "doc.source",
                    NewDocMetadata(
                        "doc.source",
                        new DocumentColumnMetadata(
                            ColumnName: "related_document_id",
                            Type: ColumnType.Guid,
                            Lookup: new DocumentLookupSourceMetadata(["doc.target"]),
                            MirroredRelationship: new MirroredDocumentRelationshipMetadata("created_from")))),
                new DocumentTypeDefinition("doc.target", NewDocMetadata("doc.target"))
            ],
            relationships:
            [
                new DocumentRelationshipTypeDefinition(
                    Code: "created_from",
                    Name: "Created From",
                    IsBidirectional: false,
                    Cardinality: DocumentRelationshipCardinality.ManyToOne,
                    AllowedFromTypeCodes: ["doc.source"],
                    AllowedToTypeCodes: ["doc.target"])
            ]);

        var validator = CreateInternalValidator(registry, new AlwaysTrueIsService());

        validator.Invoking(x => x.ValidateOrThrow()).Should().NotThrow();
    }

    private static DefinitionsRegistry BuildRegistry(
        IEnumerable<DocumentTypeDefinition>? documents = null,
        IEnumerable<CatalogTypeDefinition>? catalogs = null,
        IEnumerable<DocumentRelationshipTypeDefinition>? relationships = null)
    {
        var docs = (documents ?? [])
            .ToDictionary(x => x.TypeCode, StringComparer.OrdinalIgnoreCase);

        var cats = (catalogs ?? [])
            .ToDictionary(x => x.TypeCode, StringComparer.OrdinalIgnoreCase);

        var rels = (relationships ?? [])
            .ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);

        return new DefinitionsRegistry(docs, cats, rels, new Dictionary<string, NGB.Definitions.Documents.Derivations.DocumentDerivationDefinition>(StringComparer.OrdinalIgnoreCase));
    }

    private static DocumentTypeMetadata NewDocMetadata(string typeCode, params DocumentColumnMetadata[] headColumns)
        => new(
            TypeCode: typeCode,
            Tables:
            [
                new DocumentTableMetadata(
                    TableName: typeCode.Replace(".", "_", StringComparison.Ordinal),
                    Kind: TableKind.Head,
                    Columns: headColumns)
            ],
            Presentation: new DocumentPresentationMetadata("Test"));

    private static IDefinitionsValidationService CreateInternalValidator(
        DefinitionsRegistry registry,
        IServiceProviderIsService? isService)
    {
        var runtimeAsm = typeof(IDefinitionsValidationService).Assembly;
        var impl = runtimeAsm.GetType("NGB.Runtime.Definitions.Validation.DefinitionsValidationService", throwOnError: true)!;
        var ctor = impl.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(x =>
            {
                var ps = x.GetParameters();
                return ps.Length == 2
                       && ps[0].ParameterType == typeof(DefinitionsRegistry)
                       && ps[1].ParameterType == typeof(IServiceProviderIsService);
            });

        return (IDefinitionsValidationService)ctor.Invoke([registry, isService]);
    }

    private sealed class AlwaysTrueIsService : IServiceProviderIsService
    {
        public bool IsService(Type serviceType) => true;
    }
}
