using FluentAssertions;
using NGB.Definitions.Catalogs;
using NGB.Definitions.Documents;
using NGB.Definitions.Documents.Relationships;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Metadata.Documents.Hybrid;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Definitions.Tests;

public sealed class DefinitionsFullCoverageTests
{
    private sealed class StrategyA;
    private sealed class StrategyB;

    private static DocumentTypeMetadata DocumentMetadata(string code = "doc") => new(code, []);

    private static CatalogTypeMetadata CatalogMetadata(string code = "cat")
        => new(
            CatalogCode: code,
            DisplayName: "Catalog",
            Tables: [],
            Presentation: new CatalogPresentationMetadata(code, "name"),
            Version: new CatalogMetadataVersion(1, "tests"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Document_and_catalog_registration_reject_blank_type_codes(string? typeCode)
    {
        var sut = new DefinitionsBuilder();

        ((Action)(() => sut.AddDocument(typeCode!, _ => { })))
            .Should().Throw<NgbArgumentInvalidException>();
        ((Action)(() => sut.ExtendDocument(typeCode!, _ => { })))
            .Should().Throw<NgbArgumentInvalidException>();
        ((Action)(() => sut.AddCatalog(typeCode!, _ => { })))
            .Should().Throw<NgbArgumentInvalidException>();
        ((Action)(() => sut.ExtendCatalog(typeCode!, _ => { })))
            .Should().Throw<NgbArgumentInvalidException>();
    }

    [Fact]
    public void Document_and_catalog_registration_require_configure_callbacks()
    {
        var sut = new DefinitionsBuilder();

        ((Action)(() => sut.AddDocument("doc", null!)))
            .Should().Throw<NgbArgumentRequiredException>();
        ((Action)(() => sut.ExtendDocument("doc", null!)))
            .Should().Throw<NgbArgumentRequiredException>();
        ((Action)(() => sut.AddCatalog("cat", null!)))
            .Should().Throw<NgbArgumentRequiredException>();
        ((Action)(() => sut.ExtendCatalog("cat", null!)))
            .Should().Throw<NgbArgumentRequiredException>();
    }

    [Fact]
    public void Catalog_registration_covers_duplicates_missing_metadata_and_missing_extension_target()
    {
        var sut = new DefinitionsBuilder();

        ((Action)(() => sut.AddCatalog("missing-metadata", _ => { })))
            .Should().Throw<NgbConfigurationViolationException>()
            .WithMessage("*must define metadata*");
        ((Action)(() => sut.ExtendCatalog("missing", _ => { })))
            .Should().Throw<NgbConfigurationViolationException>()
            .WithMessage("*not registered*");

        sut.AddCatalog("cat", builder => builder.Metadata(CatalogMetadata()));
        ((Action)(() => sut.AddCatalog("CAT", builder => builder.Metadata(CatalogMetadata()))))
            .Should().Throw<NgbConfigurationViolationException>()
            .WithMessage("*already registered*");
        ((Action)(() => sut.ExtendCatalog("cat", builder => builder.Metadata(CatalogMetadata()))))
            .Should().Throw<NgbConfigurationViolationException>()
            .WithMessage("*already has metadata*");
    }

    [Fact]
    public void Catalog_builder_covers_nulls_duplicate_storage_and_duplicate_validators()
    {
        var sut = new DefinitionsBuilder();
        sut.AddCatalog("cat", builder => builder
            .Metadata(CatalogMetadata())
            .TypedStorage<StrategyA>()
            .AddValidator<StrategyA>()
            .AddValidator<StrategyA>()
            .AddValidator<StrategyB>());

        ((Action)(() => sut.ExtendCatalog("cat", builder => builder.Metadata(null!))))
            .Should().Throw<NgbArgumentRequiredException>();
        ((Action)(() => sut.ExtendCatalog("cat", builder => builder.TypedStorage(null!))))
            .Should().Throw<NgbArgumentRequiredException>();
        ((Action)(() => sut.ExtendCatalog("cat", builder => builder.TypedStorage<StrategyB>())))
            .Should().Throw<NgbConfigurationViolationException>()
            .WithMessage("*typed storage*already configured*");
        ((Action)(() => sut.ExtendCatalog("cat", builder => builder.AddValidator(null!))))
            .Should().Throw<NgbArgumentRequiredException>();

        var definition = sut.Build().GetCatalog("CAT");
        definition.TypedStorageType.Should().Be(typeof(StrategyA));
        definition.ValidatorTypes.Should().Equal(typeof(StrategyA), typeof(StrategyB));
    }

    [Fact]
    public void Document_builder_covers_all_strategies_null_guards_duplicates_and_validators()
    {
        var sut = new DefinitionsBuilder();
        sut.AddDocument("doc", builder => builder
            .Metadata(DocumentMetadata())
            .TypedStorage<StrategyA>()
            .PostingHandler<StrategyA>()
            .OperationalRegisterPostingHandler<StrategyA>()
            .ReferenceRegisterPostingHandler<StrategyA>()
            .NumberingPolicy<StrategyA>()
            .ApprovalPolicy<StrategyA>()
            .AddDraftValidator<StrategyA>()
            .AddDraftValidator<StrategyA>()
            .AddDraftValidator<StrategyB>()
            .AddPostValidator<StrategyA>()
            .AddPostValidator<StrategyA>()
            .AddPostValidator<StrategyB>());

        var definition = sut.Build().GetDocument("DOC");
        definition.TypedStorageType.Should().Be(typeof(StrategyA));
        definition.PostingHandlerType.Should().Be(typeof(StrategyA));
        definition.OperationalRegisterPostingHandlerType.Should().Be(typeof(StrategyA));
        definition.ReferenceRegisterPostingHandlerType.Should().Be(typeof(StrategyA));
        definition.NumberingPolicyType.Should().Be(typeof(StrategyA));
        definition.ApprovalPolicyType.Should().Be(typeof(StrategyA));
        definition.DraftValidatorTypes.Should().Equal(typeof(StrategyA), typeof(StrategyB));
        definition.PostValidatorTypes.Should().Equal(typeof(StrategyA), typeof(StrategyB));

        AssertDocumentBuilderNullGuard(sut, builder => builder.Metadata(null!));
        AssertDocumentBuilderNullGuard(sut, builder => builder.TypedStorage(null!));
        AssertDocumentBuilderNullGuard(sut, builder => builder.PostingHandler(null!));
        AssertDocumentBuilderNullGuard(sut, builder => builder.OperationalRegisterPostingHandler(null!));
        AssertDocumentBuilderNullGuard(sut, builder => builder.ReferenceRegisterPostingHandler(null!));
        AssertDocumentBuilderNullGuard(sut, builder => builder.NumberingPolicy(null!));
        AssertDocumentBuilderNullGuard(sut, builder => builder.ApprovalPolicy(null!));
        AssertDocumentBuilderNullGuard(sut, builder => builder.AddDraftValidator(null!));
        AssertDocumentBuilderNullGuard(sut, builder => builder.AddPostValidator(null!));

        AssertDocumentBuilderDuplicateGuard(sut, builder => builder.TypedStorage<StrategyB>());
        AssertDocumentBuilderDuplicateGuard(sut, builder => builder.PostingHandler<StrategyB>());
        AssertDocumentBuilderDuplicateGuard(sut, builder => builder.OperationalRegisterPostingHandler<StrategyB>());
        AssertDocumentBuilderDuplicateGuard(sut, builder => builder.ReferenceRegisterPostingHandler<StrategyB>());
        AssertDocumentBuilderDuplicateGuard(sut, builder => builder.NumberingPolicy<StrategyB>());
        AssertDocumentBuilderDuplicateGuard(sut, builder => builder.ApprovalPolicy<StrategyB>());
    }

    [Fact]
    public void Definition_value_objects_validate_required_arguments_and_default_collections()
    {
        ((Action)(() => new DocumentTypeDefinition(" ", DocumentMetadata())))
            .Should().Throw<NgbArgumentInvalidException>();
        ((Action)(() => new DocumentTypeDefinition("doc", null!)))
            .Should().Throw<NgbArgumentRequiredException>();
        var document = new DocumentTypeDefinition("doc", DocumentMetadata());
        document.TypeCode.Should().Be("doc");
        document.Metadata.Should().NotBeNull();
        document.DraftValidatorTypes.Should().BeEmpty();
        document.PostValidatorTypes.Should().BeEmpty();

        ((Action)(() => new CatalogTypeDefinition(" ", CatalogMetadata())))
            .Should().Throw<NgbArgumentInvalidException>();
        ((Action)(() => new CatalogTypeDefinition("cat", null!)))
            .Should().Throw<NgbArgumentRequiredException>();
        var catalog = new CatalogTypeDefinition("cat", CatalogMetadata());
        catalog.TypeCode.Should().Be("cat");
        catalog.Metadata.Should().NotBeNull();
        catalog.ValidatorTypes.Should().BeEmpty();
    }

    [Theory]
    [InlineData(DocumentRelationshipCardinality.ManyToMany, null, null)]
    [InlineData(DocumentRelationshipCardinality.OneToMany, null, 1)]
    [InlineData(DocumentRelationshipCardinality.ManyToOne, 1, null)]
    [InlineData(DocumentRelationshipCardinality.OneToOne, 1, 1)]
    public void Relationship_cardinality_exposes_expected_limits(
        DocumentRelationshipCardinality cardinality,
        int? outgoing,
        int? incoming)
    {
        var definition = new DocumentRelationshipTypeDefinition(
            "relationship", "Relationship", false, cardinality, null, null);

        definition.MaxOutgoingPerFrom.Should().Be(outgoing);
        definition.MaxIncomingPerTo.Should().Be(incoming);
    }

    [Fact]
    public void Relationship_registration_builds_trims_deduplicates_and_extends_a_definition()
    {
        var sut = new DefinitionsBuilder();
        sut.AddDocumentRelationshipType("  parent-child  ", builder => builder
            .Name("Parent / child")
            .ManyToMany()
            .Bidirectional(false)
            .AllowFromDocumentTypes(" doc.a ", "DOC.A")
            .AllowToDocumentTypes(" doc.b ", "DOC.B"));
        sut.ExtendDocumentRelationshipType("PARENT-CHILD", builder => builder
            .OneToOne()
            .Bidirectional()
            .AllowFromDocumentTypes("doc.c")
            .AllowToDocumentTypes("doc.d"));

        var definition = sut.Build().GetDocumentRelationshipType("parent-child");
        definition.Name.Should().Be("Parent / child");
        definition.Cardinality.Should().Be(DocumentRelationshipCardinality.OneToOne);
        definition.IsBidirectional.Should().BeTrue();
        definition.AllowedFromTypeCodes.Should().BeEquivalentTo("doc.a", "doc.c");
        definition.AllowedToTypeCodes.Should().BeEquivalentTo("doc.b", "doc.d");
    }

    [Fact]
    public void Relationship_builder_convenience_methods_cover_every_cardinality()
    {
        var sut = new DefinitionsBuilder();
        sut.AddDocumentRelationshipType("one-many", b => b.Name("one-many").OneToMany());
        sut.AddDocumentRelationshipType("many-one", b => b.Name("many-one").ManyToOne());

        var registry = sut.Build();
        registry.GetDocumentRelationshipType("one-many").Cardinality
            .Should().Be(DocumentRelationshipCardinality.OneToMany);
        registry.GetDocumentRelationshipType("many-one").Cardinality
            .Should().Be(DocumentRelationshipCardinality.ManyToOne);
    }

    [Fact]
    public void Relationship_registration_rejects_invalid_inputs_and_invalid_configuration()
    {
        var sut = new DefinitionsBuilder();

        AssertInvalidRelationshipCode(sut, null!);
        AssertInvalidRelationshipCode(sut, " ");
        AssertInvalidRelationshipCode(sut, new string('r', 129));
        ((Action)(() => sut.AddDocumentRelationshipType("r", null!)))
            .Should().Throw<NgbArgumentRequiredException>();
        ((Action)(() => sut.AddDocumentRelationshipType("r", _ => { })))
            .Should().Throw<NgbConfigurationViolationException>().WithMessage("*Name*");
        ((Action)(() => sut.AddDocumentRelationshipType("r", b => b.Name("name"))))
            .Should().Throw<NgbConfigurationViolationException>().WithMessage("*Cardinality*");

        sut.AddDocumentRelationshipType("r", b => b.Name("name").ManyToMany());
        ((Action)(() => sut.AddDocumentRelationshipType("R", b => b.Name("name").ManyToMany())))
            .Should().Throw<NgbConfigurationViolationException>().WithMessage("*already registered*");
        ((Action)(() => sut.ExtendDocumentRelationshipType("missing", _ => { })))
            .Should().Throw<NgbConfigurationViolationException>().WithMessage("*not registered*");
        ((Action)(() => sut.ExtendDocumentRelationshipType("r", null!)))
            .Should().Throw<NgbArgumentRequiredException>();
    }

    [Fact]
    public void Relationship_builder_rejects_null_and_blank_allowed_type_codes()
    {
        var sut = new DefinitionsBuilder();
        sut.AddDocumentRelationshipType("r", b => b.Name("name").ManyToMany());

        ((Action)(() => sut.ExtendDocumentRelationshipType("r", b => b.AllowFromDocumentTypes(null!))))
            .Should().Throw<NgbArgumentRequiredException>();
        ((Action)(() => sut.ExtendDocumentRelationshipType("r", b => b.AllowToDocumentTypes(null!))))
            .Should().Throw<NgbArgumentRequiredException>();
        ((Action)(() => sut.ExtendDocumentRelationshipType("r", b => b.AllowFromDocumentTypes(" "))))
            .Should().Throw<NgbArgumentInvalidException>();
        ((Action)(() => sut.ExtendDocumentRelationshipType("r", b => b.AllowToDocumentTypes(" "))))
            .Should().Throw<NgbArgumentInvalidException>();
    }

    [Fact]
    public void Derivation_registration_builds_trims_deduplicates_and_extends_a_definition()
    {
        var sut = new DefinitionsBuilder();
        sut.AddDocumentDerivation("  create-child  ", builder => builder
            .Name("Create child")
            .From(" source ")
            .To(" target ")
            .Relationship(" based-on ")
            .Relationships("BASED-ON", "related")
            .Handler<StrategyA>());
        sut.ExtendDocumentDerivation("CREATE-CHILD", builder => builder.Handler(typeof(StrategyB)));

        var definition = sut.Build().GetDocumentDerivation("create-child");
        definition.Name.Should().Be("Create child");
        definition.FromTypeCode.Should().Be("source");
        definition.ToTypeCode.Should().Be("target");
        definition.RelationshipCodes.Should().Equal("based-on", "related");
        definition.HandlerType.Should().Be(typeof(StrategyB));
    }

    [Fact]
    public void Derivation_registration_rejects_invalid_inputs_duplicates_and_missing_configuration()
    {
        var sut = new DefinitionsBuilder();

        AssertInvalidDerivationCode(sut, null!);
        AssertInvalidDerivationCode(sut, " ");
        AssertInvalidDerivationCode(sut, new string('d', 129));
        ((Action)(() => sut.AddDocumentDerivation("d", null!)))
            .Should().Throw<NgbArgumentRequiredException>();
        AssertInvalidDerivationConfiguration(sut, _ => { }, "*Name*");
        AssertInvalidDerivationConfiguration(sut, b => b.Name("name"), "*FromTypeCode*");
        AssertInvalidDerivationConfiguration(sut, b => b.Name("name").From("from"), "*ToTypeCode*");
        AssertInvalidDerivationConfiguration(sut, b => b.Name("name").From("from").To("to"), "*relationship*");

        sut.AddDocumentDerivation("d", CompleteDerivation);
        ((Action)(() => sut.AddDocumentDerivation("D", CompleteDerivation)))
            .Should().Throw<NgbConfigurationViolationException>().WithMessage("*already registered*");
        ((Action)(() => sut.ExtendDocumentDerivation("missing", _ => { })))
            .Should().Throw<NgbConfigurationViolationException>().WithMessage("*not registered*");
        ((Action)(() => sut.ExtendDocumentDerivation("d", null!)))
            .Should().Throw<NgbArgumentRequiredException>();
    }

    [Fact]
    public void Derivation_relationships_reject_null_blank_and_overlong_codes()
    {
        var sut = new DefinitionsBuilder();
        sut.AddDocumentDerivation("d", CompleteDerivation);

        ((Action)(() => sut.ExtendDocumentDerivation("d", b => b.Relationships(null!))))
            .Should().Throw<NgbArgumentRequiredException>();
        ((Action)(() => sut.ExtendDocumentDerivation("d", b => b.Relationship(" "))))
            .Should().Throw<NgbArgumentInvalidException>();
        ((Action)(() => sut.ExtendDocumentDerivation("d", b => b.Relationship(new string('r', 129)))))
            .Should().Throw<NgbArgumentInvalidException>();
    }

    [Fact]
    public void Registry_exposes_all_collections_and_covers_success_missing_and_invalid_lookups()
    {
        var builder = new DefinitionsBuilder();
        builder.AddDocument("doc", b => b.Metadata(DocumentMetadata()));
        builder.AddCatalog("cat", b => b.Metadata(CatalogMetadata()));
        builder.AddDocumentRelationshipType("rel", b => b.Name("Relationship").ManyToMany());
        builder.AddDocumentDerivation("derive", CompleteDerivation);
        var sut = builder.Build();

        sut.Documents.Should().ContainSingle();
        sut.Catalogs.Should().ContainSingle();
        sut.DocumentRelationshipTypes.Should().ContainSingle();
        sut.DocumentDerivations.Should().ContainSingle();

        sut.TryGetDocument("DOC", out _).Should().BeTrue();
        sut.TryGetCatalog("CAT", out _).Should().BeTrue();
        sut.TryGetDocumentRelationshipType("REL", out _).Should().BeTrue();
        sut.TryGetDocumentDerivation("DERIVE", out _).Should().BeTrue();

        sut.TryGetDocument("missing", out _).Should().BeFalse();
        sut.TryGetCatalog("missing", out _).Should().BeFalse();
        sut.TryGetDocumentRelationshipType("missing", out _).Should().BeFalse();
        sut.TryGetDocumentDerivation("missing", out _).Should().BeFalse();

        ((Action)(() => sut.GetDocument("missing"))).Should().Throw<NgbConfigurationViolationException>();
        ((Action)(() => sut.GetCatalog("missing"))).Should().Throw<NgbConfigurationViolationException>();
        ((Action)(() => sut.GetDocumentRelationshipType("missing"))).Should().Throw<NgbConfigurationViolationException>();
        ((Action)(() => sut.GetDocumentDerivation("missing"))).Should().Throw<NgbConfigurationViolationException>();

        ((Action)(() => sut.TryGetDocument(" ", out _))).Should().Throw<NgbArgumentInvalidException>();
        ((Action)(() => sut.TryGetCatalog(" ", out _))).Should().Throw<NgbArgumentInvalidException>();
        ((Action)(() => sut.TryGetDocumentRelationshipType(" ", out _))).Should().Throw<NgbArgumentInvalidException>();
        ((Action)(() => sut.TryGetDocumentDerivation(" ", out _))).Should().Throw<NgbArgumentInvalidException>();
    }

    [Fact]
    public void Registry_constructor_requires_every_dictionary()
    {
        var documents = new Dictionary<string, DocumentTypeDefinition>();
        var catalogs = new Dictionary<string, CatalogTypeDefinition>();
        var relationships = new Dictionary<string, DocumentRelationshipTypeDefinition>();
        var derivations = new Dictionary<string, NGB.Definitions.Documents.Derivations.DocumentDerivationDefinition>();

        ((Action)(() => new DefinitionsRegistry(null!, catalogs, relationships, derivations)))
            .Should().Throw<NgbArgumentRequiredException>();
        ((Action)(() => new DefinitionsRegistry(documents, null!, relationships, derivations)))
            .Should().Throw<NgbArgumentRequiredException>();
        ((Action)(() => new DefinitionsRegistry(documents, catalogs, null!, derivations)))
            .Should().Throw<NgbArgumentRequiredException>();
        ((Action)(() => new DefinitionsRegistry(documents, catalogs, relationships, null!)))
            .Should().Throw<NgbArgumentRequiredException>();
    }

    private static void AssertDocumentBuilderNullGuard(
        DefinitionsBuilder sut,
        Action<DocumentTypeDefinitionBuilder> configure)
        => ((Action)(() => sut.ExtendDocument("doc", configure)))
            .Should().Throw<NgbArgumentRequiredException>();

    private static void AssertDocumentBuilderDuplicateGuard(
        DefinitionsBuilder sut,
        Action<DocumentTypeDefinitionBuilder> configure)
        => ((Action)(() => sut.ExtendDocument("doc", configure)))
            .Should().Throw<NgbConfigurationViolationException>();

    private static void AssertInvalidRelationshipCode(DefinitionsBuilder sut, string relationshipCode)
    {
        ((Action)(() => sut.AddDocumentRelationshipType(relationshipCode, _ => { })))
            .Should().Throw<NgbArgumentInvalidException>();
        ((Action)(() => sut.ExtendDocumentRelationshipType(relationshipCode, _ => { })))
            .Should().Throw<NgbArgumentInvalidException>();
    }

    private static void AssertInvalidDerivationCode(DefinitionsBuilder sut, string derivationCode)
    {
        ((Action)(() => sut.AddDocumentDerivation(derivationCode, _ => { })))
            .Should().Throw<NgbArgumentInvalidException>();
        ((Action)(() => sut.ExtendDocumentDerivation(derivationCode, _ => { })))
            .Should().Throw<NgbArgumentInvalidException>();
    }

    private static void AssertInvalidDerivationConfiguration(
        DefinitionsBuilder sut,
        Action<NGB.Definitions.Documents.Derivations.DocumentDerivationDefinitionBuilder> configure,
        string message)
        => ((Action)(() => sut.AddDocumentDerivation(Guid.NewGuid().ToString("N"), configure)))
            .Should().Throw<NgbConfigurationViolationException>().WithMessage(message);

    private static void CompleteDerivation(
        NGB.Definitions.Documents.Derivations.DocumentDerivationDefinitionBuilder builder)
        => builder.Name("Derivation").From("from").To("to").Relationship("relationship");
}
