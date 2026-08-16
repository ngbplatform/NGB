using FluentAssertions;
using NGB.Definitions;
using NGB.Definitions.Documents.Derivations;
using NGB.Definitions.Documents.Relationships;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Metadata.Documents.Hybrid;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Definitions;

public sealed class DefinitionsBuilderFullCoverageTests
{
    [Fact]
    public void Document_registration_covers_guards_required_metadata_duplicates_extensions_and_build()
    {
        AssertThrows<NgbArgumentInvalidException>(builder => builder.AddDocument(" ", _ => { }));
        AssertThrows<NgbArgumentRequiredException>(builder => builder.AddDocument("doc", null!));
        AssertThrows<NgbConfigurationViolationException>(builder => builder.AddDocument("doc", _ => { }));
        AssertThrows<NgbArgumentInvalidException>(builder => builder.ExtendDocument("", _ => { }));
        AssertThrows<NgbArgumentRequiredException>(builder => builder.ExtendDocument("missing", null!));
        AssertThrows<NgbConfigurationViolationException>(builder => builder.ExtendDocument("missing", _ => { }));

        var sut = new DefinitionsBuilder();
        sut.AddDocument("doc", document => document
            .Metadata(DocumentMetadata("doc"))
            .TypedStorage<string>()
            .PostingHandler<object>()
            .OperationalRegisterPostingHandler<Uri>()
            .ReferenceRegisterPostingHandler<Version>()
            .NumberingPolicy<Exception>()
            .ApprovalPolicy<Stream>()
            .AddDraftValidator<Random>()
            .AddDraftValidator<Random>()
            .AddPostValidator<HttpClient>()
            .AddPostValidator<HttpClient>());
        sut.ExtendDocument("DOC", document =>
        {
            document.AddDraftValidator(typeof(int));
            document.AddPostValidator(typeof(long));
        });
        AssertThrows<NgbConfigurationViolationException>(builder =>
        {
            builder.AddDocument("doc", d => d.Metadata(DocumentMetadata("doc")));
            builder.AddDocument("DOC", d => d.Metadata(DocumentMetadata("DOC")));
        });

        var definition = sut.Build().GetDocument("DOC");
        definition.TypeCode.Should().Be("doc");
        definition.TypedStorageType.Should().Be(typeof(string));
        definition.PostingHandlerType.Should().Be(typeof(object));
        definition.OperationalRegisterPostingHandlerType.Should().Be(typeof(Uri));
        definition.ReferenceRegisterPostingHandlerType.Should().Be(typeof(Version));
        definition.NumberingPolicyType.Should().Be(typeof(Exception));
        definition.ApprovalPolicyType.Should().Be(typeof(Stream));
        definition.DraftValidatorTypes.Should().Equal(typeof(Random), typeof(int));
        definition.PostValidatorTypes.Should().Equal(typeof(HttpClient), typeof(long));
    }

    [Fact]
    public void Document_definition_builder_rejects_null_and_duplicate_singleton_bindings()
    {
        AssertDocumentConfigurationThrows<NgbArgumentRequiredException>(d => d.Metadata(null!));
        AssertDocumentConfigurationThrows<NgbConfigurationViolationException>(d =>
            d.Metadata(DocumentMetadata("doc")).Metadata(DocumentMetadata("doc")));

        AssertDocumentConfigurationThrows<NgbArgumentRequiredException>(d => d.TypedStorage(null!));
        AssertDocumentConfigurationThrows<NgbConfigurationViolationException>(d =>
            d.TypedStorage(typeof(string)).TypedStorage<object>());
        AssertDocumentConfigurationThrows<NgbArgumentRequiredException>(d => d.PostingHandler(null!));
        AssertDocumentConfigurationThrows<NgbConfigurationViolationException>(d =>
            d.PostingHandler(typeof(string)).PostingHandler<object>());
        AssertDocumentConfigurationThrows<NgbArgumentRequiredException>(d => d.OperationalRegisterPostingHandler(null!));
        AssertDocumentConfigurationThrows<NgbConfigurationViolationException>(d =>
            d.OperationalRegisterPostingHandler(typeof(string)).OperationalRegisterPostingHandler<object>());
        AssertDocumentConfigurationThrows<NgbArgumentRequiredException>(d => d.ReferenceRegisterPostingHandler(null!));
        AssertDocumentConfigurationThrows<NgbConfigurationViolationException>(d =>
            d.ReferenceRegisterPostingHandler(typeof(string)).ReferenceRegisterPostingHandler<object>());
        AssertDocumentConfigurationThrows<NgbArgumentRequiredException>(d => d.NumberingPolicy(null!));
        AssertDocumentConfigurationThrows<NgbConfigurationViolationException>(d =>
            d.NumberingPolicy(typeof(string)).NumberingPolicy<object>());
        AssertDocumentConfigurationThrows<NgbArgumentRequiredException>(d => d.ApprovalPolicy(null!));
        AssertDocumentConfigurationThrows<NgbConfigurationViolationException>(d =>
            d.ApprovalPolicy(typeof(string)).ApprovalPolicy<object>());
        AssertDocumentConfigurationThrows<NgbArgumentRequiredException>(d => d.AddDraftValidator(null!));
        AssertDocumentConfigurationThrows<NgbArgumentRequiredException>(d => d.AddPostValidator(null!));
    }

    [Fact]
    public void Catalog_registration_and_builder_cover_guards_duplicates_extensions_and_bindings()
    {
        AssertThrows<NgbArgumentInvalidException>(builder => builder.AddCatalog(" ", _ => { }));
        AssertThrows<NgbArgumentRequiredException>(builder => builder.AddCatalog("catalog", null!));
        AssertThrows<NgbConfigurationViolationException>(builder => builder.AddCatalog("catalog", _ => { }));
        AssertThrows<NgbArgumentInvalidException>(builder => builder.ExtendCatalog("", _ => { }));
        AssertThrows<NgbArgumentRequiredException>(builder => builder.ExtendCatalog("missing", null!));
        AssertThrows<NgbConfigurationViolationException>(builder => builder.ExtendCatalog("missing", _ => { }));

        AssertCatalogConfigurationThrows<NgbArgumentRequiredException>(c => c.Metadata(null!));
        AssertCatalogConfigurationThrows<NgbConfigurationViolationException>(c =>
            c.Metadata(CatalogMetadata("catalog")).Metadata(CatalogMetadata("catalog")));
        AssertCatalogConfigurationThrows<NgbArgumentRequiredException>(c => c.TypedStorage(null!));
        AssertCatalogConfigurationThrows<NgbConfigurationViolationException>(c =>
            c.TypedStorage(typeof(string)).TypedStorage<object>());
        AssertCatalogConfigurationThrows<NgbArgumentRequiredException>(c => c.AddValidator(null!));

        var sut = new DefinitionsBuilder();
        sut.AddCatalog("catalog", catalog => catalog
            .Metadata(CatalogMetadata("catalog"))
            .TypedStorage<string>()
            .AddValidator<Uri>()
            .AddValidator<Uri>());
        sut.ExtendCatalog("CATALOG", catalog => catalog.AddValidator(typeof(long)));
        Action duplicate = () => sut.AddCatalog("CATALOG", c => c.Metadata(CatalogMetadata("CATALOG")));
        duplicate.Should().Throw<NgbConfigurationViolationException>();

        var definition = sut.Build().GetCatalog("CATALOG");
        definition.TypedStorageType.Should().Be(typeof(string));
        definition.ValidatorTypes.Should().Equal(typeof(Uri), typeof(long));
    }

    [Fact]
    public void Relationship_registration_covers_normalization_required_fields_cardinalities_and_allowed_types()
    {
        AssertThrows<NgbArgumentInvalidException>(builder => builder.AddDocumentRelationshipType(" ", _ => { }));
        AssertThrows<NgbArgumentInvalidException>(builder =>
            builder.AddDocumentRelationshipType(new string('r', 129), _ => { }));
        AssertThrows<NgbArgumentRequiredException>(builder => builder.AddDocumentRelationshipType("rel", null!));
        AssertThrows<NgbConfigurationViolationException>(builder =>
            builder.AddDocumentRelationshipType("rel", r => r.ManyToMany()));
        AssertThrows<NgbConfigurationViolationException>(builder =>
            builder.AddDocumentRelationshipType("rel", r => r.Name("Relationship")));
        AssertThrows<NgbArgumentInvalidException>(builder => builder.ExtendDocumentRelationshipType("", _ => { }));
        AssertThrows<NgbArgumentRequiredException>(builder => builder.ExtendDocumentRelationshipType("missing", null!));
        AssertThrows<NgbConfigurationViolationException>(builder =>
            builder.ExtendDocumentRelationshipType("missing", _ => { }));

        AssertRelationshipConfigurationThrows<NgbArgumentRequiredException>(r => r.AllowFromDocumentTypes(null!));
        AssertRelationshipConfigurationThrows<NgbArgumentInvalidException>(r => r.AllowFromDocumentTypes(" "));
        AssertRelationshipConfigurationThrows<NgbArgumentRequiredException>(r => r.AllowToDocumentTypes(null!));
        AssertRelationshipConfigurationThrows<NgbArgumentInvalidException>(r => r.AllowToDocumentTypes(""));

        var sut = new DefinitionsBuilder();
        sut.AddDocumentRelationshipType(" many ", relationship => relationship
            .Name("Many")
            .Bidirectional()
            .ManyToMany()
            .AllowFromDocumentTypes()
            .AllowToDocumentTypes());
        sut.AddDocumentRelationshipType("one-many", relationship => relationship.Name("One many").OneToMany());
        sut.AddDocumentRelationshipType("many-one", relationship => relationship.Name("Many one").ManyToOne());
        sut.AddDocumentRelationshipType("one-one", relationship => relationship
            .Name("One one")
            .Bidirectional(false)
            .OneToOne()
            .AllowFromDocumentTypes(" doc ", "DOC")
            .AllowToDocumentTypes("target"));
        sut.ExtendDocumentRelationshipType("MANY", relationship => relationship.AllowFromDocumentTypes("source"));
        Action duplicate = () => sut.AddDocumentRelationshipType("MANY", r => r.Name("Duplicate").ManyToMany());
        duplicate.Should().Throw<NgbConfigurationViolationException>();

        var registry = sut.Build();
        var many = registry.GetDocumentRelationshipType("many");
        many.IsBidirectional.Should().BeTrue();
        many.Cardinality.Should().Be(DocumentRelationshipCardinality.ManyToMany);
        many.AllowedFromTypeCodes.Should().Equal("source");
        many.AllowedToTypeCodes.Should().BeNull();
        registry.GetDocumentRelationshipType("one-one").AllowedFromTypeCodes.Should().Equal("doc");
    }

    [Fact]
    public void Derivation_registration_covers_normalization_required_fields_relationship_deduplication_and_handler()
    {
        AssertThrows<NgbArgumentInvalidException>(builder => builder.AddDocumentDerivation(" ", _ => { }));
        AssertThrows<NgbArgumentInvalidException>(builder =>
            builder.AddDocumentDerivation(new string('d', 129), _ => { }));
        AssertThrows<NgbArgumentRequiredException>(builder => builder.AddDocumentDerivation("derive", null!));
        AssertDerivationConfigurationThrows<NgbConfigurationViolationException>(d =>
            d.From("source").To("target").Relationship("rel"));
        AssertDerivationConfigurationThrows<NgbConfigurationViolationException>(d =>
            d.Name("Derive").To("target").Relationship("rel"));
        AssertDerivationConfigurationThrows<NgbConfigurationViolationException>(d =>
            d.Name("Derive").From("source").Relationship("rel"));
        AssertDerivationConfigurationThrows<NgbConfigurationViolationException>(d =>
            d.Name("Derive").From("source").To("target"));
        AssertThrows<NgbArgumentInvalidException>(builder => builder.ExtendDocumentDerivation("", _ => { }));
        AssertThrows<NgbArgumentRequiredException>(builder => builder.ExtendDocumentDerivation("missing", null!));
        AssertThrows<NgbConfigurationViolationException>(builder => builder.ExtendDocumentDerivation("missing", _ => { }));
        AssertDerivationConfigurationThrows<NgbArgumentInvalidException>(d => d.Relationship(" "));
        AssertDerivationConfigurationThrows<NgbArgumentInvalidException>(d => d.Relationship(new string('r', 129)));
        AssertDerivationConfigurationThrows<NgbArgumentRequiredException>(d => d.Relationships(null!));

        var sut = new DefinitionsBuilder();
        sut.AddDocumentDerivation(" derive ", derivation => derivation
            .Name("Derive")
            .From(" source ")
            .To(" target ")
            .Relationships(" rel ", "REL")
            .Handler<string>());
        sut.AddDocumentDerivation("direct-handler", derivation => derivation
            .Name("Direct")
            .From("source")
            .To("target")
            .Relationship("rel")
            .Handler(typeof(int)));
        sut.ExtendDocumentDerivation("DERIVE", derivation => derivation.Relationship("second"));
        Action duplicate = () => sut.AddDocumentDerivation("DERIVE", CompleteDerivation);
        duplicate.Should().Throw<NgbConfigurationViolationException>();

        var definition = sut.Build().GetDocumentDerivation("derive");
        definition.FromTypeCode.Should().Be("source");
        definition.ToTypeCode.Should().Be("target");
        definition.RelationshipCodes.Should().Equal("rel", "second");
        definition.HandlerType.Should().Be(typeof(string));
    }

    [Fact]
    public void Empty_builder_produces_an_empty_registry()
    {
        var registry = new DefinitionsBuilder().Build();
        registry.Documents.Should().BeEmpty();
        registry.Catalogs.Should().BeEmpty();
        registry.DocumentRelationshipTypes.Should().BeEmpty();
        registry.DocumentDerivations.Should().BeEmpty();
    }

    private static void AssertDocumentConfigurationThrows<TException>(Action<DocumentTypeDefinitionBuilder> configure)
        where TException : Exception
    {
        Action act = () => new DefinitionsBuilder().AddDocument("doc", configure);
        act.Should().Throw<TException>();
    }

    private static void AssertCatalogConfigurationThrows<TException>(Action<CatalogTypeDefinitionBuilder> configure)
        where TException : Exception
    {
        Action act = () => new DefinitionsBuilder().AddCatalog("catalog", configure);
        act.Should().Throw<TException>();
    }

    private static void AssertRelationshipConfigurationThrows<TException>(
        Action<DocumentRelationshipTypeDefinitionBuilder> configure)
        where TException : Exception
    {
        Action act = () => new DefinitionsBuilder().AddDocumentRelationshipType("rel", relationship =>
        {
            relationship.Name("Relationship").ManyToMany();
            configure(relationship);
        });
        act.Should().Throw<TException>();
    }

    private static void AssertDerivationConfigurationThrows<TException>(
        Action<DocumentDerivationDefinitionBuilder> configure)
        where TException : Exception
    {
        Action act = () => new DefinitionsBuilder().AddDocumentDerivation("derive", configure);
        act.Should().Throw<TException>();
    }

    private static void AssertThrows<TException>(Action<DefinitionsBuilder> action)
        where TException : Exception
    {
        Action act = () => action(new DefinitionsBuilder());
        act.Should().Throw<TException>();
    }

    private static void CompleteDerivation(DocumentDerivationDefinitionBuilder derivation)
        => derivation.Name("Derive").From("source").To("target").Relationship("rel");

    private static DocumentTypeMetadata DocumentMetadata(string code)
        => new(code, Array.Empty<DocumentTableMetadata>());

    private static CatalogTypeMetadata CatalogMetadata(string code)
        => new(
            code,
            $"Catalog {code}",
            Array.Empty<CatalogTableMetadata>(),
            new CatalogPresentationMetadata(code, "name"),
            new CatalogMetadataVersion(1, "tests"));
}
