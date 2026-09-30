using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Definitions;
using NGB.Definitions.Documents.Validation;
using NGB.Metadata.Documents.Hybrid;
using NGB.Runtime.Documents.Validation;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Documents.Validation;

public sealed class DefinitionsDocumentValidatorResolverTests
{
    [Fact]
    public void ResolveDraftValidators_ReturnsValidators_FromDefinitionAndDI()
    {
        var builder = new DefinitionsBuilder();
        builder.AddDocument("DOC", d =>
        {
            d.Metadata(new DocumentTypeMetadata(
                "DOC",
                Array.Empty<DocumentTableMetadata>()));

            d.AddDraftValidator<TestDraftValidator>();
            d.AddPostValidator<TestPostValidator>();
        });

        var services = new ServiceCollection();
        services.AddSingleton(builder.Build());
        services.AddScoped<TestDraftValidator>();
        services.AddScoped<TestPostValidator>();
        services.AddScoped<IDocumentDraftValidator>(sp => sp.GetRequiredService<TestDraftValidator>());
        services.AddScoped<IDocumentPostValidator>(sp => sp.GetRequiredService<TestPostValidator>());
        services.AddScoped<IDocumentValidatorResolver, DefinitionsDocumentValidatorResolver>();

        using var sp = services.BuildServiceProvider();

        var resolver = sp.GetRequiredService<IDocumentValidatorResolver>();

        var draftValidators = resolver.ResolveDraftValidators("DOC");
        draftValidators.Should().HaveCount(1);
        draftValidators[0].Should().BeOfType<TestDraftValidator>();
        resolver.ResolveDraftValidators("doc").Should().BeSameAs(draftValidators);

        var postValidators = resolver.ResolvePostValidators("DOC");
        postValidators.Should().HaveCount(1);
        postValidators[0].Should().BeOfType<TestPostValidator>();
        resolver.ResolvePostValidators("doc").Should().BeSameAs(postValidators);
    }

    [Fact]
    public void ResolveValidators_MissingDefinition_ReturnsEmpty()
    {
        var defs = new DefinitionsBuilder().Build();
        var services = new ServiceCollection();
        services.AddSingleton(defs);
        services.AddScoped<IDocumentValidatorResolver, DefinitionsDocumentValidatorResolver>();

        using var sp = services.BuildServiceProvider();

        var resolver = sp.GetRequiredService<IDocumentValidatorResolver>();

        resolver.ResolveDraftValidators("UNKNOWN").Should().BeEmpty();
        resolver.ResolvePostValidators("UNKNOWN").Should().BeEmpty();
    }

    [Fact]
    public void ResolveValidators_KnownDefinitionWithoutBindings_ReturnsEmpty()
    {
        var builder = new DefinitionsBuilder();
        builder.AddDocument("DOC", d => d.Metadata(new DocumentTypeMetadata("DOC", [])));
        var resolver = new DefinitionsDocumentValidatorResolver(
            builder.Build(),
            Array.Empty<IDocumentDraftValidator>(),
            Array.Empty<IDocumentPostValidator>());

        resolver.ResolveDraftValidators("DOC").Should().BeEmpty();
        resolver.ResolvePostValidators("DOC").Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveValidators_BlankTypeCode_Throws(string? typeCode)
    {
        var resolver = new DefinitionsDocumentValidatorResolver(
            new DefinitionsBuilder().Build(),
            Array.Empty<IDocumentDraftValidator>(),
            Array.Empty<IDocumentPostValidator>());

        Action draft = () => resolver.ResolveDraftValidators(typeCode!);
        Action post = () => resolver.ResolvePostValidators(typeCode!);

        draft.Should().Throw<NgbArgumentRequiredException>().Which.ParamName.Should().Be("typeCode");
        post.Should().Throw<NgbArgumentRequiredException>().Which.ParamName.Should().Be("typeCode");
    }

    [Fact]
    public void ResolveDraftValidators_BindingWithWrongContract_ThrowsConfigurationError()
    {
        var resolver = Resolver(
            draftTypes: [typeof(string)],
            draftValidators: []);

        var action = () => resolver.ResolveDraftValidators("DOC");

        action.Should().Throw<NgbConfigurationViolationException>()
            .WithMessage("*must implement IDocumentDraftValidator*");
    }

    [Fact]
    public void ResolveDraftValidators_MissingRegistration_ThrowsConfigurationError()
    {
        var resolver = Resolver(
            draftTypes: [typeof(TestDraftValidator)],
            draftValidators: []);

        var action = () => resolver.ResolveDraftValidators("DOC");

        action.Should().Throw<NgbConfigurationViolationException>()
            .WithMessage("*is not registered*");
    }

    [Fact]
    public void ResolveDraftValidators_DuplicateRegistrations_ThrowsConfigurationError()
    {
        var resolver = Resolver(
            draftTypes: [typeof(TestDraftValidator)],
            draftValidators: [new TestDraftValidator(), new TestDraftValidator()]);

        var action = () => resolver.ResolveDraftValidators("DOC");

        action.Should().Throw<NgbConfigurationViolationException>()
            .WithMessage("*multiple registrations*");
    }

    [Fact]
    public void ResolveValidators_TypeCodeMismatch_Throws()
    {
        var builder = new DefinitionsBuilder();
        builder.AddDocument("DOC", d =>
        {
            d.Metadata(new DocumentTypeMetadata(
                "DOC",
                Array.Empty<DocumentTableMetadata>()));

            d.AddDraftValidator<MismatchedDraftValidator>();
        });

        var services = new ServiceCollection();
        services.AddSingleton(builder.Build());
        services.AddScoped<MismatchedDraftValidator>();
        services.AddScoped<IDocumentDraftValidator>(sp => sp.GetRequiredService<MismatchedDraftValidator>());
        services.AddScoped<IDocumentValidatorResolver, DefinitionsDocumentValidatorResolver>();

        using var sp = services.BuildServiceProvider();
        var resolver = sp.GetRequiredService<IDocumentValidatorResolver>();

        var act = () => resolver.ResolveDraftValidators("DOC");
        act.Should().Throw<NgbConfigurationViolationException>()
            .WithMessage("*TypeCode*does not match*");
    }

    private static DefinitionsDocumentValidatorResolver Resolver(
        IReadOnlyList<Type> draftTypes,
        IReadOnlyList<IDocumentDraftValidator> draftValidators)
    {
        var definition = new NGB.Definitions.Documents.DocumentTypeDefinition(
            "DOC",
            new DocumentTypeMetadata("DOC", []),
            draftValidatorTypes: draftTypes);
        var registry = new DefinitionsRegistry(
            new Dictionary<string, NGB.Definitions.Documents.DocumentTypeDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["DOC"] = definition
            },
            new Dictionary<string, NGB.Definitions.Catalogs.CatalogTypeDefinition>(),
            new Dictionary<string, NGB.Definitions.Documents.Relationships.DocumentRelationshipTypeDefinition>(),
            new Dictionary<string, NGB.Definitions.Documents.Derivations.DocumentDerivationDefinition>());

        return new DefinitionsDocumentValidatorResolver(
            registry,
            draftValidators,
            Array.Empty<IDocumentPostValidator>());
    }

    private sealed class TestDraftValidator : IDocumentDraftValidator
    {
        public string TypeCode => "DOC";

        public Task ValidateCreateDraftAsync(NGB.Core.Documents.DocumentRecord draft, CancellationToken ct)
            => Task.CompletedTask;
    }

    private sealed class TestPostValidator : IDocumentPostValidator
    {
        public string TypeCode => "DOC";

        public Task ValidateBeforePostAsync(NGB.Core.Documents.DocumentRecord documentForUpdate, CancellationToken ct)
            => Task.CompletedTask;
    }

    private sealed class MismatchedDraftValidator : IDocumentDraftValidator
    {
        public string TypeCode => "NOT_DOC";

        public Task ValidateCreateDraftAsync(NGB.Core.Documents.DocumentRecord draft, CancellationToken ct)
            => Task.CompletedTask;
    }
}
