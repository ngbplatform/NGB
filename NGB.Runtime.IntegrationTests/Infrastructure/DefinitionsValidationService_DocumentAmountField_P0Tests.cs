using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Definitions;
using NGB.Definitions.Catalogs;
using NGB.Definitions.Documents;
using NGB.Metadata.Base;
using NGB.Metadata.Documents.Hybrid;
using NGB.Runtime.Definitions.Validation;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

public sealed class DefinitionsValidationService_DocumentAmountField_P0Tests
{
    [Fact]
    public void ValidateOrThrow_WhenAmountFieldIsInvalid_ReportsConfigurationErrors()
    {
        var registry = BuildRegistry(
            documents:
            [
                new DocumentTypeDefinition(
                    "doc.bad_string_amount",
                    NewDocMetadata(
                        "doc.bad_string_amount",
                        new DocumentPresentationMetadata("Bad String Amount", AmountField: "total_due"),
                        new DocumentColumnMetadata("display", ColumnType.String),
                        new DocumentColumnMetadata("total_due", ColumnType.String))),
                new DocumentTypeDefinition(
                    "doc.bad_part_amount",
                    new DocumentTypeMetadata(
                        TypeCode: "doc.bad_part_amount",
                        Tables:
                        [
                            new DocumentTableMetadata(
                                TableName: "doc_bad_part_amount",
                                Kind: TableKind.Head,
                                Columns:
                                [
                                    new DocumentColumnMetadata("display", ColumnType.String)
                                ]),
                            new DocumentTableMetadata(
                                TableName: "doc_bad_part_amount__lines",
                                Kind: TableKind.Part,
                                PartCode: "lines",
                                Columns:
                                [
                                    new DocumentColumnMetadata("amount", ColumnType.Decimal)
                                ])
                        ],
                        Presentation: new DocumentPresentationMetadata("Bad Part Amount", AmountField: "amount"))),
                new DocumentTypeDefinition(
                    "doc.bad_trimmed_amount",
                    NewDocMetadata(
                        "doc.bad_trimmed_amount",
                        new DocumentPresentationMetadata("Bad Trimmed Amount", AmountField: " total_due "),
                        new DocumentColumnMetadata("display", ColumnType.String),
                        new DocumentColumnMetadata("total_due", ColumnType.Decimal)))
            ]);

        var validator = CreateInternalValidator(registry, new AlwaysTrueIsService());

        var act = () => validator.ValidateOrThrow();

        var ex = act.Should().Throw<DefinitionsValidationException>().Which;
        ex.Errors.Should().HaveCount(3);
        ex.Errors.Should().Contain(e => e.Contains("Document 'doc.bad_string_amount': Presentation.AmountField 'total_due'") && e.Contains("Decimal, Int32, or Int64"));
        ex.Errors.Should().Contain(e => e.Contains("Document 'doc.bad_part_amount': Presentation.AmountField 'amount'") && e.Contains("existing numeric head-table column"));
        ex.Errors.Should().Contain(e => e.Contains("Document 'doc.bad_trimmed_amount': Presentation.AmountField") && e.Contains("non-empty trimmed head-field name"));
    }

    [Fact]
    public void ValidateOrThrow_WhenAmountFieldIsValid_DoesNotAddErrors()
    {
        var registry = BuildRegistry(
            documents:
            [
                new DocumentTypeDefinition(
                    "doc.good_amount",
                    NewDocMetadata(
                        "doc.good_amount",
                        new DocumentPresentationMetadata("Good Amount", AmountField: "total_due"),
                        new DocumentColumnMetadata("display", ColumnType.String),
                        new DocumentColumnMetadata("total_due", ColumnType.Decimal)))
            ]);

        var validator = CreateInternalValidator(registry, new AlwaysTrueIsService());

        validator.Invoking(x => x.ValidateOrThrow()).Should().NotThrow();
    }

    private static DefinitionsRegistry BuildRegistry(
        IEnumerable<DocumentTypeDefinition>? documents = null,
        IEnumerable<CatalogTypeDefinition>? catalogs = null)
    {
        var docs = (documents ?? [])
            .ToDictionary(x => x.TypeCode, StringComparer.OrdinalIgnoreCase);

        var cats = (catalogs ?? [])
            .ToDictionary(x => x.TypeCode, StringComparer.OrdinalIgnoreCase);

        return new DefinitionsRegistry(
            docs,
            cats,
            new Dictionary<string, NGB.Definitions.Documents.Relationships.DocumentRelationshipTypeDefinition>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, NGB.Definitions.Documents.Derivations.DocumentDerivationDefinition>(StringComparer.OrdinalIgnoreCase));
    }

    private static DocumentTypeMetadata NewDocMetadata(
        string typeCode,
        DocumentPresentationMetadata presentation,
        params DocumentColumnMetadata[] headColumns)
        => new(
            TypeCode: typeCode,
            Tables:
            [
                new DocumentTableMetadata(
                    TableName: typeCode.Replace(".", "_", StringComparison.Ordinal),
                    Kind: TableKind.Head,
                    Columns: headColumns)
            ],
            Presentation: presentation);

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
