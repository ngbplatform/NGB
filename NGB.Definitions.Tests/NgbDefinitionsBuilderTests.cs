using FluentAssertions;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Metadata.Documents.Hybrid;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Definitions.Tests;

public sealed class NgbDefinitionsBuilderTests
{
    private static DocumentTypeMetadata DocMeta(string code)
        => new(code, Array.Empty<DocumentTableMetadata>());

    private static CatalogTypeMetadata CatMeta(string code)
        => new(
            CatalogCode: code,
            DisplayName: "Test",
            Tables: Array.Empty<CatalogTableMetadata>(),
            Presentation: new CatalogPresentationMetadata(code, "name"),
            Version: new CatalogMetadataVersion(1, "tests"));

    private sealed class DocStorageA { }
    private sealed class DocPostingA { }
    private sealed class DocNumberingA { }
    private sealed class DocApprovalA { }
    private sealed class DocValidatorA { }
    private sealed class DocValidatorB { }

    private sealed class CatStorageA { }
    private sealed class CatValidatorA { }

    [Fact]
    public void AddDocument_RequiresMetadata()
    {
        var b = new DefinitionsBuilder();

        var act = () => b.AddDocument("DOC", _ => { });

        act.Should().Throw<NgbConfigurationViolationException>()
            .WithMessage("*DOC*metadata*");
    }

    [Fact]
    public void AddDocument_DuplicateTypeCode_FailsFast()
    {
        var b = new DefinitionsBuilder();
        b.AddDocument("DOC", d => d.Metadata(DocMeta("DOC")));

        var act = () => b.AddDocument("DOC", d => d.Metadata(DocMeta("DOC")));

        act.Should().Throw<NgbConfigurationViolationException>()
            .WithMessage("*already registered*");
    }

    [Fact]
    public void ExtendDocument_WithoutAdd_FailsFast()
    {
        var b = new DefinitionsBuilder();

        var act = () => b.ExtendDocument("DOC", d => d.Metadata(DocMeta("DOC")));

        act.Should().Throw<NgbConfigurationViolationException>()
            .WithMessage("*not registered*AddDocument*");
    }

    [Fact]
    public void ExtendDocument_CannotOverrideMetadata()
    {
        var b = new DefinitionsBuilder();
        b.AddDocument("DOC", d => d.Metadata(DocMeta("DOC")));

        var act = () => b.ExtendDocument("DOC", d => d.Metadata(DocMeta("DOC")));

        act.Should().Throw<NgbConfigurationViolationException>()
            .WithMessage("*already has metadata*");
    }

    [Fact]
    public void ExtendDocument_CanAddStrategies_AndValidators_AreDeduplicated()
    {
        var b = new DefinitionsBuilder();
        b.AddDocument("DOC", d => d.Metadata(DocMeta("DOC")));

        b.ExtendDocument("DOC", d => d
            .TypedStorage<DocStorageA>()
            .PostingHandler<DocPostingA>()
            .NumberingPolicy<DocNumberingA>()
            .ApprovalPolicy<DocApprovalA>()
            .AddDraftValidator<DocValidatorA>()
            .AddDraftValidator<DocValidatorA>()
            .AddDraftValidator<DocValidatorB>()
            .AddPostValidator<DocValidatorA>()
            .AddPostValidator<DocValidatorA>());

        var r = b.Build();
        var def = r.GetDocument("DOC");

        def.TypedStorageType.Should().Be(typeof(DocStorageA));
        def.PostingHandlerType.Should().Be(typeof(DocPostingA));
        def.NumberingPolicyType.Should().Be(typeof(DocNumberingA));
        def.ApprovalPolicyType.Should().Be(typeof(DocApprovalA));

        def.DraftValidatorTypes.Should().Equal(new[] { typeof(DocValidatorA), typeof(DocValidatorB) });
        def.PostValidatorTypes.Should().Equal(new[] { typeof(DocValidatorA) });
    }

    [Fact]
    public void ExtendDocument_CannotOverride_Strategies()
    {
        var b = new DefinitionsBuilder();
        b.AddDocument("DOC", d => d.Metadata(DocMeta("DOC")).TypedStorage<DocStorageA>());

        var act = () => b.ExtendDocument("DOC", d => d.TypedStorage<DocStorageA>());

        act.Should().Throw<NgbConfigurationViolationException>()
            .WithMessage("*typed storage*already*");
    }

    [Fact]
    public void ExtendCatalog_CanConfigureTypedStorage_AndValidators_AreDeduplicated()
    {
        var b = new DefinitionsBuilder();
        b.AddCatalog("CAT", c => c.Metadata(CatMeta("CAT")));

        b.ExtendCatalog("CAT", c => c
            .TypedStorage<CatStorageA>()
            .AddValidator<CatValidatorA>()
            .AddValidator<CatValidatorA>());

        var def = b.Build().GetCatalog("CAT");
        def.TypedStorageType.Should().Be(typeof(CatStorageA));
        def.ValidatorTypes.Should().Equal(new[] { typeof(CatValidatorA) });
    }

    [Fact]
    public void Registry_Lookup_IsCaseInsensitive()
    {
        var b = new DefinitionsBuilder();
        b.AddDocument("doc", d => d.Metadata(DocMeta("doc")));
        b.AddCatalog("cat", c => c.Metadata(CatMeta("cat")));

        var r = b.Build();

        r.GetDocument("DOC").TypeCode.Should().Be("doc");
        r.GetCatalog("CAT").TypeCode.Should().Be("cat");
    }
}
