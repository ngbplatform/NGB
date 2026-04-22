using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Definitions;
using NGB.Definitions.Catalogs;
using NGB.Definitions.Documents;
using NGB.Definitions.Documents.Relationships;
using NGB.Metadata.Base;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Metadata.Documents.Hybrid;
using NGB.Runtime.Definitions.Validation;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

public sealed class DefinitionsValidationService_PartCodes_P0Tests
{
    [Fact]
    public void ValidateOrThrow_WhenDocumentPartTableOmitsPartCode_FailsFast()
    {
        var registry = BuildRegistry(
            documents:
            [
                new DocumentTypeDefinition(
                    typeCode: "it.doc.partcode.required",
                    metadata: new DocumentTypeMetadata(
                        TypeCode: "it.doc.partcode.required",
                        Tables:
                        [
                            new DocumentTableMetadata(
                                TableName: "it_doc_partcode_required",
                                Kind: TableKind.Head,
                                Columns:
                                [
                                    new DocumentColumnMetadata("document_id", ColumnType.Guid, Required: true),
                                    new DocumentColumnMetadata("display", ColumnType.String, Required: true)
                                ]),
                            new DocumentTableMetadata(
                                TableName: "it_doc_partcode_required__storage_rows",
                                Kind: TableKind.Part,
                                Columns:
                                [
                                    new DocumentColumnMetadata("document_id", ColumnType.Guid, Required: true),
                                    new DocumentColumnMetadata("ordinal", ColumnType.Int32, Required: true)
                                ])
                        ],
                        Presentation: new DocumentPresentationMetadata("Test")))
            ]);

        var validator = CreateInternalValidator(registry);

        var ex = Assert.Throws<DefinitionsValidationException>(() => validator.ValidateOrThrow());
        ex.Errors.Should().Contain(e => e.Contains("must declare a non-empty PartCode.", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateOrThrow_WhenDocumentPartCodesDuplicate_FailsFast()
    {
        var registry = BuildRegistry(
            documents:
            [
                new DocumentTypeDefinition(
                    typeCode: "it.doc.partcode.duplicate",
                    metadata: new DocumentTypeMetadata(
                        TypeCode: "it.doc.partcode.duplicate",
                        Tables:
                        [
                            new DocumentTableMetadata(
                                TableName: "it_doc_partcode_duplicate",
                                Kind: TableKind.Head,
                                Columns:
                                [
                                    new DocumentColumnMetadata("document_id", ColumnType.Guid, Required: true),
                                    new DocumentColumnMetadata("display", ColumnType.String, Required: true)
                                ]),
                            new DocumentTableMetadata(
                                TableName: "it_doc_partcode_duplicate__storage_a",
                                Kind: TableKind.Part,
                                Columns:
                                [
                                    new DocumentColumnMetadata("document_id", ColumnType.Guid, Required: true),
                                    new DocumentColumnMetadata("ordinal", ColumnType.Int32, Required: true)
                                ],
                                PartCode: "lines"),
                            new DocumentTableMetadata(
                                TableName: "it_doc_partcode_duplicate__storage_b",
                                Kind: TableKind.Part,
                                Columns:
                                [
                                    new DocumentColumnMetadata("document_id", ColumnType.Guid, Required: true),
                                    new DocumentColumnMetadata("ordinal", ColumnType.Int32, Required: true)
                                ],
                                PartCode: "lines")
                        ],
                        Presentation: new DocumentPresentationMetadata("Test")))
            ]);

        var validator = CreateInternalValidator(registry);

        var ex = Assert.Throws<DefinitionsValidationException>(() => validator.ValidateOrThrow());
        ex.Errors.Should().Contain(e => e.Contains("declares duplicate PartCode 'lines'.", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateOrThrow_WhenCatalogHeadTableDeclaresPartCode_FailsFast()
    {
        var registry = BuildRegistry(
            catalogs:
            [
                new CatalogTypeDefinition(
                    typeCode: "it.cat.partcode.head",
                    metadata: new CatalogTypeMetadata(
                        CatalogCode: "it.cat.partcode.head",
                        DisplayName: "Test",
                        Tables:
                        [
                            new CatalogTableMetadata(
                                TableName: "it_cat_partcode_head",
                                Kind: TableKind.Head,
                                Columns:
                                [
                                    new CatalogColumnMetadata("catalog_id", ColumnType.Guid, Required: true),
                                    new CatalogColumnMetadata("name", ColumnType.String, Required: true)
                                ],
                                Indexes: [],
                                PartCode: "oops")
                        ],
                        Presentation: new CatalogPresentationMetadata("it_cat_partcode_head", "name"),
                        Version: new CatalogMetadataVersion(1, "tests")))
            ]);

        var validator = CreateInternalValidator(registry);

        var ex = Assert.Throws<DefinitionsValidationException>(() => validator.ValidateOrThrow());
        ex.Errors.Should().Contain(e => e.Contains("is not a part table and cannot declare PartCode.", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateOrThrow_WhenCatalogPartCodeIsNotTrimmed_FailsFast()
    {
        var registry = BuildRegistry(
            catalogs:
            [
                new CatalogTypeDefinition(
                    typeCode: "it.cat.partcode.trim",
                    metadata: new CatalogTypeMetadata(
                        CatalogCode: "it.cat.partcode.trim",
                        DisplayName: "Test",
                        Tables:
                        [
                            new CatalogTableMetadata(
                                TableName: "it_cat_partcode_trim",
                                Kind: TableKind.Head,
                                Columns:
                                [
                                    new CatalogColumnMetadata("catalog_id", ColumnType.Guid, Required: true),
                                    new CatalogColumnMetadata("name", ColumnType.String, Required: true)
                                ],
                                Indexes: []),
                            new CatalogTableMetadata(
                                TableName: "it_cat_partcode_trim__contact_storage",
                                Kind: TableKind.Part,
                                Columns:
                                [
                                    new CatalogColumnMetadata("catalog_id", ColumnType.Guid, Required: true),
                                    new CatalogColumnMetadata("ordinal", ColumnType.Int32, Required: true)
                                ],
                                Indexes: [],
                                PartCode: " contacts ")
                        ],
                        Presentation: new CatalogPresentationMetadata("it_cat_partcode_trim", "name"),
                        Version: new CatalogMetadataVersion(1, "tests")))
            ]);

        var validator = CreateInternalValidator(registry);

        var ex = Assert.Throws<DefinitionsValidationException>(() => validator.ValidateOrThrow());
        ex.Errors.Should().Contain(e => e.Contains("must declare a trimmed PartCode.", StringComparison.Ordinal));
    }

    private static DefinitionsRegistry BuildRegistry(
        IEnumerable<DocumentTypeDefinition>? documents = null,
        IEnumerable<CatalogTypeDefinition>? catalogs = null)
        => new(
            documents: (documents ?? []).ToDictionary(x => x.TypeCode, StringComparer.OrdinalIgnoreCase),
            catalogs: (catalogs ?? []).ToDictionary(x => x.TypeCode, StringComparer.OrdinalIgnoreCase),
            documentRelationshipTypes: new Dictionary<string, DocumentRelationshipTypeDefinition>(StringComparer.OrdinalIgnoreCase),
            documentDerivations: new Dictionary<string, NGB.Definitions.Documents.Derivations.DocumentDerivationDefinition>(StringComparer.OrdinalIgnoreCase));

    private static IDefinitionsValidationService CreateInternalValidator(DefinitionsRegistry registry)
    {
        var runtimeAsm = typeof(IDefinitionsValidationService).Assembly;
        var impl = runtimeAsm.GetType("NGB.Runtime.Definitions.Validation.DefinitionsValidationService", throwOnError: true)!;
        var ctors = impl.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var ctor = ctors.Single(c =>
        {
            var ps = c.GetParameters();
            return ps.Length == 2
                   && ps[0].ParameterType == typeof(DefinitionsRegistry)
                   && ps[1].ParameterType == typeof(IServiceProviderIsService);
        });

        return (IDefinitionsValidationService)ctor.Invoke([registry, null!]);
    }
}
