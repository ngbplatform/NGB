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
public sealed class Definitions_StartupValidation_FailsFast_DerivationRelationshipCodes_MaxLen128_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task HostStart_WhenDerivationRelationshipCodeExceedsMaxLen_FailsFast()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString, services =>
        {
            services.RemoveAll<DefinitionsRegistry>();
            services.AddSingleton(_ => CreateRegistry());
        });

        Func<Task> act = async () => await host.StartAsync();

        var ex = await act.Should().ThrowAsync<DefinitionsValidationException>();
        ex.Which.Errors.Should().Contain(e => e.Contains("exceeding max length 128", StringComparison.OrdinalIgnoreCase));
    }

    private static DefinitionsRegistry CreateRegistry()
    {
        var docs = new Dictionary<string, DocumentTypeDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["doc_from"] = new("doc_from", TestDocMetadata.Create("doc_from")),
            ["doc_to"] = new("doc_to", TestDocMetadata.Create("doc_to")),
        };

        // Keep relationship types valid; the derivation will reference an invalid (too long) code.
        var relTypes = new Dictionary<string, DocumentRelationshipTypeDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["based_on"] = new("based_on", "Based on", false, DocumentRelationshipCardinality.ManyToMany, null, null),
        };

        var tooLong = new string('x', 129);

        var deriv = new DocumentDerivationDefinition(
            Code: "der_x",
            Name: "Derivation X",
            FromTypeCode: "doc_from",
            ToTypeCode: "doc_to",
            RelationshipCodes: new List<string> { tooLong },
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
