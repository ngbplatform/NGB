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
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

[Collection(PostgresCollection.Name)]
public sealed class Definitions_StartupValidation_FailsFast_DerivationRelationshipCodes_TrimmedAndDistinct_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task HostStart_WhenDerivationRelationshipCodesHaveWhitespaceOrDuplicates_FailsFast()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString, services =>
        {
            services.RemoveAll<DefinitionsRegistry>();
            services.AddSingleton(_ => CreateRegistry());
        });

        Func<Task> act = async () => await host.StartAsync();

        var ex = await act.Should().ThrowAsync<DefinitionsValidationException>();
        ex.Which.Errors.Should().Contain(e => e.Contains("non-trimmed code", StringComparison.OrdinalIgnoreCase));
        ex.Which.Errors.Should().Contain(e => e.Contains("duplicate code", StringComparison.OrdinalIgnoreCase));
    }

    private static DefinitionsRegistry CreateRegistry()
    {
        var docs = new Dictionary<string, DocumentTypeDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["doc_from"] = new("doc_from", TestDocMetadata.Create("doc_from")),
            ["doc_to"] = new("doc_to", TestDocMetadata.Create("doc_to")),
        };

        var relTypes = new Dictionary<string, DocumentRelationshipTypeDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["based_on"] = new("based_on", "Based on", false, DocumentRelationshipCardinality.ManyToMany, null, null),
            ["created_from"] = new("created_from", "Created from", false, DocumentRelationshipCardinality.ManyToOne, null, null),
        };

        var deriv = new DocumentDerivationDefinition(
            Code: "der_x",
            Name: "Derivation X",
            FromTypeCode: "doc_from",
            ToTypeCode: "doc_to",
            RelationshipCodes: new List<string> { "based_on", " based_on ", "CREATED_FROM", "created_from" },
            HandlerType: null);

        var derivations = new Dictionary<string, DocumentDerivationDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["der_x"] = deriv,
        };

        return new DefinitionsRegistry(
            documents: docs,
            catalogs: new Dictionary<string, CatalogTypeDefinition>(StringComparer.OrdinalIgnoreCase),
            documentRelationshipTypes: relTypes,
            documentDerivations: derivations);
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
}
