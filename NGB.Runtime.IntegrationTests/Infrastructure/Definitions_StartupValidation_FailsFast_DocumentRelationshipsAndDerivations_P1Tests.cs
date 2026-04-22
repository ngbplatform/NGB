using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Posting.Validators;
using NGB.Definitions;
using NGB.Metadata.Documents.Hybrid;
using NGB.Runtime.DependencyInjection;
using NGB.PostgreSql.DependencyInjection;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

public sealed class Definitions_StartupValidation_FailsFast_DocumentRelationshipsAndDerivations_P1Tests(PostgresTestFixture fixture)
    : IClassFixture<PostgresTestFixture>
{
    [Fact]
    public async Task StartAsync_WhenBidirectionalRelationshipHasDifferentAllowedTypeSets_FailsFast()
    {
        using var host = CreateHost(new BidirectionalRelationshipMismatchContributor());

        var act = () => host.StartAsync(CancellationToken.None);

        await act.Should().ThrowAsync<Exception>()
            .WithMessage("*Definitions validation failed*DocumentRelationshipType 'it_rel.bidir_mismatch'*Bidirectional relationship must have identical AllowedFromTypeCodes and AllowedToTypeCodes*");
    }

    [Fact]
    public async Task StartAsync_WhenDerivationReferencesUnknownRelationshipType_FailsFast()
    {
        using var host = CreateHost(new DerivationUnknownRelationshipContributor());

        var act = () => host.StartAsync(CancellationToken.None);

        await act.Should().ThrowAsync<Exception>()
            .WithMessage("*Definitions validation failed*DocumentDerivation 'it_deriv.unknown_rel'*unknown relationship type*it_rel.unknown*");
    }

    private IHost CreateHost(IDefinitionsContributor contributor)
    {
        return Host.CreateDefaultBuilder()
            .UseDefaultServiceProvider(o =>
            {
                o.ValidateScopes = true;
                o.ValidateOnBuild = true;
            })
            .ConfigureServices(services =>
            {
                services.AddNgbRuntime();
                services.AddNgbPostgres(fixture.ConnectionString);
                services.AddScoped<IAccountingPostingValidator, BasicAccountingPostingValidator>();

                services.AddSingleton<IDefinitionsContributor>(contributor);
            })
            .Build();
    }

    private sealed class BidirectionalRelationshipMismatchContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument(
                typeCode: "it_rel.doc_a",
                configure: d => d.Metadata(new DocumentTypeMetadata(
                    "it_rel.doc_a",
                    Array.Empty<DocumentTableMetadata>(),
                    new DocumentPresentationMetadata("IT Relationship Doc A"),
                    new DocumentMetadataVersion(1, "it-tests"))));

            builder.AddDocument(
                typeCode: "it_rel.doc_b",
                configure: d => d.Metadata(new DocumentTypeMetadata(
                    "it_rel.doc_b",
                    Array.Empty<DocumentTableMetadata>(),
                    new DocumentPresentationMetadata("IT Relationship Doc B"),
                    new DocumentMetadataVersion(1, "it-tests"))));

            builder.AddDocumentRelationshipType(
                relationshipCode: "it_rel.bidir_mismatch",
                configure: r => r
                    .Name("IT Bidirectional Mismatch")
                    .ManyToMany()
                    .Bidirectional(true)
                    .AllowFromDocumentTypes("it_rel.doc_a")
                    .AllowToDocumentTypes("it_rel.doc_b"));
        }
    }

    private sealed class DerivationUnknownRelationshipContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument(
                typeCode: "it_deriv.source",
                configure: d => d.Metadata(new DocumentTypeMetadata(
                    "it_deriv.source",
                    Array.Empty<DocumentTableMetadata>(),
                    new DocumentPresentationMetadata("IT Derivation Source"),
                    new DocumentMetadataVersion(1, "it-tests"))));

            builder.AddDocument(
                typeCode: "it_deriv.target",
                configure: d => d.Metadata(new DocumentTypeMetadata(
                    "it_deriv.target",
                    Array.Empty<DocumentTableMetadata>(),
                    new DocumentPresentationMetadata("IT Derivation Target"),
                    new DocumentMetadataVersion(1, "it-tests"))));

            builder.AddDocumentDerivation(
                derivationCode: "it_deriv.unknown_rel",
                configure: d => d
                    .Name("IT Unknown Relationship")
                    .From("it_deriv.source")
                    .To("it_deriv.target")
                    .Relationship("it_rel.unknown"));
        }
    }
}
