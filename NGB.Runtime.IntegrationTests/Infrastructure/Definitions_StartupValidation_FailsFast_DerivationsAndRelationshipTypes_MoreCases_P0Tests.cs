using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NGB.Definitions;
using NGB.Definitions.Catalogs;
using NGB.Definitions.Documents;
using NGB.Definitions.Documents.Derivations;
using NGB.Definitions.Documents.Relationships;
using NGB.Metadata.Documents.Hybrid;
using NGB.Runtime.Definitions.Validation;
using NGB.Runtime.Documents.Derivations;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

[Collection(PostgresCollection.Name)]
public sealed class Definitions_StartupValidation_FailsFast_DerivationsAndRelationshipTypes_MoreCases_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task HostStart_WhenDocumentDerivationsHaveSemanticViolations_FailsFast_WithAggregatedErrors()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString, services =>
        {
            services.RemoveAll<DefinitionsRegistry>();
            services.AddSingleton(_ => CreateRegistry_WithBadDerivations());
        });

        Func<Task> act = async () => await host.StartAsync();

        var ex = await act.Should().ThrowAsync<DefinitionsValidationException>();
        ex.Which.Errors.Should().Contain(e => e.Contains("DocumentDerivation: Code must be", StringComparison.Ordinal));
        ex.Which.Errors.Should().Contain(e => e.Contains("Code exceeds max length 128", StringComparison.Ordinal));
        ex.Which.Errors.Should().Contain(e => e.Contains("Name must be non-empty", StringComparison.Ordinal));
        ex.Which.Errors.Should().Contain(e => e.Contains("FromTypeCode references unknown document type", StringComparison.Ordinal));
        ex.Which.Errors.Should().Contain(e => e.Contains("ToTypeCode must be non-empty", StringComparison.Ordinal));
        ex.Which.Errors.Should().Contain(e => e.Contains("RelationshipCodes contains an empty code", StringComparison.Ordinal));
        ex.Which.Errors.Should().Contain(e => e.Contains("references unknown relationship type", StringComparison.Ordinal));
        ex.Which.Errors.Should().Contain(e =>
            e.Contains("DocumentDerivation", StringComparison.Ordinal)
            && e.Contains("HandlerType", StringComparison.Ordinal)
            && e.Contains(nameof(IDocumentDerivationHandler), StringComparison.Ordinal));
    }

    [Fact]
    public async Task HostStart_WhenDerivationRelationshipCodesIsNullOrEmpty_FailsFast()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString, services =>
        {
            services.RemoveAll<DefinitionsRegistry>();
            services.AddSingleton(_ => CreateRegistry_DerivationWithNullRelationshipCodes());
        });

        Func<Task> act = async () => await host.StartAsync();

        var ex = await act.Should().ThrowAsync<DefinitionsValidationException>();
        ex.Which.Errors.Should().Contain(e => e.Contains("RelationshipCodes must contain at least one code", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HostStart_WhenDerivationHandlerTypeIsValidButNotRegisteredInDI_FailsFast()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString, services =>
        {
            services.RemoveAll<DefinitionsRegistry>();
            services.AddSingleton(_ => CreateRegistry_DerivationWithValidButNotRegisteredHandler());
        });

        Func<Task> act = async () => await host.StartAsync();

        var ex = await act.Should().ThrowAsync<DefinitionsValidationException>();
        ex.Which.Errors.Should().Contain(e =>
            e.Contains("DocumentDerivation 'ok_der'", StringComparison.Ordinal)
            && e.Contains("HandlerType", StringComparison.Ordinal)
            && e.Contains("not registered in DI", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task HostStart_WhenRelationshipTypeAllowedTypeCodesAreInvalid_FailsFast_WithAggregatedErrors()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString, services =>
        {
            services.RemoveAll<DefinitionsRegistry>();
            services.AddSingleton(_ => CreateRegistry_WithBadRelationshipTypes());
        });

        Func<Task> act = async () => await host.StartAsync();

        var ex = await act.Should().ThrowAsync<DefinitionsValidationException>();
        ex.Which.Errors.Should().Contain(e => e.Contains("DocumentRelationshipType: Code must be a non-empty trimmed string", StringComparison.Ordinal));
        ex.Which.Errors.Should().Contain(e => e.Contains("contains an empty TypeCode", StringComparison.Ordinal));
        ex.Which.Errors.Should().Contain(e => e.Contains("references unknown document type", StringComparison.Ordinal));
        ex.Which.Errors.Should().Contain(e => e.Contains("Bidirectional relationship must have identical AllowedFromTypeCodes and AllowedToTypeCodes", StringComparison.Ordinal));
        ex.Which.Errors.Should().Contain(e => e.Contains("Name must be non-empty", StringComparison.Ordinal));
    }

    private static DefinitionsRegistry CreateRegistry_WithBadDerivations()
    {
        var docs = new Dictionary<string, DocumentTypeDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["doc_a"] = new("doc_a", TestDocMetadata.Create("doc_a")),
        };

        // No relationship types registered here on purpose.
        var relTypes = new Dictionary<string, DocumentRelationshipTypeDefinition>(StringComparer.OrdinalIgnoreCase);

        var bad1 = new DocumentDerivationDefinition(
            Code: " bad ",
            Name: "",
            FromTypeCode: "unknown_from",
            ToTypeCode: "   ",
            RelationshipCodes: new List<string> { "", " missing_rel " },
            HandlerType: typeof(BadDerivationHandler));

        var bad2 = new DocumentDerivationDefinition(
            Code: new string('x', 129),
            Name: "Derivation X",
            FromTypeCode: "unknown_from_2",
            ToTypeCode: "doc_a",
            RelationshipCodes: new List<string> { "missing_rel_2" },
            HandlerType: null);

        var derivations = new Dictionary<string, DocumentDerivationDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            [bad1.Code] = bad1,
            [bad2.Code] = bad2,
        };

        return new DefinitionsRegistry(
            documents: docs,
            catalogs: new Dictionary<string, CatalogTypeDefinition>(StringComparer.OrdinalIgnoreCase),
            documentRelationshipTypes: relTypes,
            documentDerivations: derivations);
    }

    private static DefinitionsRegistry CreateRegistry_DerivationWithNullRelationshipCodes()
    {
        var docs = new Dictionary<string, DocumentTypeDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["doc_from"] = new("doc_from", TestDocMetadata.Create("doc_from")),
            ["doc_to"] = new("doc_to", TestDocMetadata.Create("doc_to")),
        };

        var relTypes = new Dictionary<string, DocumentRelationshipTypeDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["based_on"] = new("based_on", "Based on", false, DocumentRelationshipCardinality.ManyToMany, null, null),
        };

        // RelationshipCodes is intentionally null to hit the explicit null/empty validation branch.
        var deriv = new DocumentDerivationDefinition(
            Code: "der_null",
            Name: "Derivation Null",
            FromTypeCode: "doc_from",
            ToTypeCode: "doc_to",
            RelationshipCodes: null!,
            HandlerType: null);

        var derivations = new Dictionary<string, DocumentDerivationDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["der_null"] = deriv,
        };

        return new DefinitionsRegistry(
            documents: docs,
            catalogs: new Dictionary<string, CatalogTypeDefinition>(StringComparer.OrdinalIgnoreCase),
            documentRelationshipTypes: relTypes,
            documentDerivations: derivations);
    }

    private static DefinitionsRegistry CreateRegistry_DerivationWithValidButNotRegisteredHandler()
    {
        var docs = new Dictionary<string, DocumentTypeDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["doc_from"] = new("doc_from", TestDocMetadata.Create("doc_from")),
            ["doc_to"] = new("doc_to", TestDocMetadata.Create("doc_to")),
        };

        var relTypes = new Dictionary<string, DocumentRelationshipTypeDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["based_on"] = new("based_on", "Based on", false, DocumentRelationshipCardinality.ManyToMany, null, null),
        };

        var deriv = new DocumentDerivationDefinition(
            Code: "ok_der",
            Name: "Ok Derivation",
            FromTypeCode: "doc_from",
            ToTypeCode: "doc_to",
            RelationshipCodes: new List<string> { "based_on" },
            HandlerType: typeof(GoodButNotRegisteredDerivationHandler));

        var derivations = new Dictionary<string, DocumentDerivationDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["ok_der"] = deriv,
        };

        return new DefinitionsRegistry(
            documents: docs,
            catalogs: new Dictionary<string, CatalogTypeDefinition>(StringComparer.OrdinalIgnoreCase),
            documentRelationshipTypes: relTypes,
            documentDerivations: derivations);
    }

    private static DefinitionsRegistry CreateRegistry_WithBadRelationshipTypes()
    {
        var docs = new Dictionary<string, DocumentTypeDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["doc_a"] = new("doc_a", TestDocMetadata.Create("doc_a")),
            ["doc_b"] = new("doc_b", TestDocMetadata.Create("doc_b")),
        };

        var relBad = new DocumentRelationshipTypeDefinition(
            Code: " bad_rel ",
            Name: "",
            IsBidirectional: true,
            Cardinality: DocumentRelationshipCardinality.ManyToMany,
            AllowedFromTypeCodes: new[] { "", "doc_a" },
            AllowedToTypeCodes: new[] { "unknown_doc", "doc_b" });

        var relTypes = new Dictionary<string, DocumentRelationshipTypeDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            // Use the raw code as key on purpose to keep the registry internally consistent for this test.
            [relBad.Code] = relBad,
        };

        return new DefinitionsRegistry(
            documents: docs,
            catalogs: new Dictionary<string, CatalogTypeDefinition>(StringComparer.OrdinalIgnoreCase),
            documentRelationshipTypes: relTypes,
            documentDerivations: new Dictionary<string, DocumentDerivationDefinition>(StringComparer.OrdinalIgnoreCase));
    }

    private static class TestDocMetadata
    {
        public static DocumentTypeMetadata Create(string typeCode)
            => new(
                TypeCode: typeCode,
                Tables: Array.Empty<DocumentTableMetadata>(),
                Presentation: new DocumentPresentationMetadata($"IT {typeCode}"),
                Version: new DocumentMetadataVersion(1, "it-tests"));
    }

    private sealed class BadDerivationHandler
    {
        // Intentionally does not implement IDocumentDerivationHandler.
    }

    private sealed class GoodButNotRegisteredDerivationHandler : IDocumentDerivationHandler
    {
        public Task ApplyAsync(DocumentDerivationContext ctx, CancellationToken ct = default) => Task.CompletedTask;
    }
}
