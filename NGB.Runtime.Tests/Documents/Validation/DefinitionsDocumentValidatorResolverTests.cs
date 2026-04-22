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

        var postValidators = resolver.ResolvePostValidators("DOC");
        postValidators.Should().HaveCount(1);
        postValidators[0].Should().BeOfType<TestPostValidator>();
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
