using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Definitions;
using NGB.Definitions.Documents.Numbering;
using NGB.Metadata.Documents.Hybrid;
using NGB.Runtime.Documents.Policies;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Documents.Policies;

public sealed class DefinitionsDocumentNumberingPolicyResolverTests
{
    [Fact]
    public void Constructor_when_required_dependency_is_null_throws()
    {
        var definitions = new DefinitionsBuilder().Build();

        Action nullDefinitions = () => new DefinitionsDocumentNumberingPolicyResolver(
            null!,
            Array.Empty<IDocumentNumberingPolicy>());
        Action nullPolicies = () => new DefinitionsDocumentNumberingPolicyResolver(definitions, null!);

        nullDefinitions.Should().Throw<NgbArgumentRequiredException>()
            .Which.ParamName.Should().Be("definitions");
        nullPolicies.Should().Throw<NgbArgumentRequiredException>()
            .Which.ParamName.Should().Be("policies");
    }

    [Fact]
    public void Resolve_returns_null_when_document_type_not_defined()
    {
        var defs = new DefinitionsBuilder().Build();
        var services = new ServiceCollection();
        services.AddSingleton(defs);
        services.AddScoped<IDocumentNumberingPolicyResolver, DefinitionsDocumentNumberingPolicyResolver>();

        using var sp = services.BuildServiceProvider();
        var resolver = sp.GetRequiredService<IDocumentNumberingPolicyResolver>();

        resolver.Resolve("doc.missing").Should().BeNull();
    }

    [Fact]
    public void Resolve_returns_null_when_policy_type_not_set()
    {
        var builder = new DefinitionsBuilder();
        builder.AddDocument("doc.test", d => d.Metadata(new DocumentTypeMetadata("doc.test", Array.Empty<DocumentTableMetadata>())));

        var services = new ServiceCollection();
        services.AddSingleton(builder.Build());
        services.AddScoped<IDocumentNumberingPolicyResolver, DefinitionsDocumentNumberingPolicyResolver>();

        using var sp = services.BuildServiceProvider();
        var resolver = sp.GetRequiredService<IDocumentNumberingPolicyResolver>();

        resolver.Resolve("doc.test").Should().BeNull();
    }

    [Fact]
    public void Resolve_returns_policy_from_definition_and_DI()
    {
        var builder = new DefinitionsBuilder();
        builder.AddDocument("doc.test", d =>
        {
            d.Metadata(new DocumentTypeMetadata("doc.test", Array.Empty<DocumentTableMetadata>()));
            d.NumberingPolicy<TestNumberingPolicy>();
        });

        var services = new ServiceCollection();
        services.AddSingleton<TestNumberingPolicy>();
        services.AddSingleton<IDocumentNumberingPolicy>(sp => sp.GetRequiredService<TestNumberingPolicy>());
        services.AddSingleton(builder.Build());
        services.AddScoped<IDocumentNumberingPolicyResolver, DefinitionsDocumentNumberingPolicyResolver>();

        using var sp = services.BuildServiceProvider();
        var resolver = sp.GetRequiredService<IDocumentNumberingPolicyResolver>();

        var policy = resolver.Resolve("doc.test");
        policy.Should().NotBeNull();
        policy!.TypeCode.Should().Be("doc.test");
        policy.EnsureNumberOnCreateDraft.Should().BeTrue();
        policy.EnsureNumberOnPost.Should().BeFalse();
        resolver.Resolve("DOC.TEST").Should().BeSameAs(policy);
    }

    [Fact]
    public void Resolve_when_policy_is_not_registered_throws()
    {
        var resolver = new DefinitionsDocumentNumberingPolicyResolver(
            DefinitionsWithPolicy<TestNumberingPolicy>(),
            Array.Empty<IDocumentNumberingPolicy>());

        Action action = () => resolver.Resolve("doc.test");

        action.Should().Throw<DocumentPolicyConfigurationException>()
            .Which.Reason.Should().Contain("not registered");
    }

    [Fact]
    public void Resolve_when_policy_has_duplicate_registrations_throws()
    {
        var resolver = new DefinitionsDocumentNumberingPolicyResolver(
            DefinitionsWithPolicy<TestNumberingPolicy>(),
            new IDocumentNumberingPolicy[] { new TestNumberingPolicy(), new TestNumberingPolicy() });

        Action action = () => resolver.Resolve("doc.test");

        action.Should().Throw<DocumentPolicyConfigurationException>()
            .Which.Reason.Should().Contain("Multiple policy registrations");
    }

    [Fact]
    public void Resolve_when_policy_type_is_not_registered_as_contract_throws()
    {
        var builder = new DefinitionsBuilder();
        builder.AddDocument("doc.test", d =>
        {
            d.Metadata(new DocumentTypeMetadata("doc.test", Array.Empty<DocumentTableMetadata>()));
            d.NumberingPolicy<NotAPolicy>();
        });

        var services = new ServiceCollection();
        services.AddSingleton<NotAPolicy>();
        services.AddSingleton(builder.Build());
        services.AddScoped<IDocumentNumberingPolicyResolver, DefinitionsDocumentNumberingPolicyResolver>();

        using var sp = services.BuildServiceProvider();
        var resolver = sp.GetRequiredService<IDocumentNumberingPolicyResolver>();

        var act = () => resolver.Resolve("doc.test");
        var ex = act.Should().Throw<DocumentPolicyConfigurationException>()
            .WithMessage("Invalid numbering policy configuration for document type 'doc.test':*")
            .Which;
        ex.ErrorCode.Should().Be(DocumentPolicyConfigurationException.ErrorCodeConst);
        ex.PolicyKind.Should().Be("numbering");
        ex.DocumentTypeCode.Should().Be("doc.test");
        ex.PolicyType.Should().Be(typeof(NotAPolicy));
        ex.Reason.Should().Contain("must implement");
    }

    [Fact]
    public void Resolve_when_type_code_mismatch_throws()
    {
        var builder = new DefinitionsBuilder();
        builder.AddDocument("doc.test", d =>
        {
            d.Metadata(new DocumentTypeMetadata("doc.test", Array.Empty<DocumentTableMetadata>()));
            d.NumberingPolicy<MismatchedPolicy>();
        });

        var services = new ServiceCollection();
        services.AddSingleton<MismatchedPolicy>();
        services.AddSingleton<IDocumentNumberingPolicy>(sp => sp.GetRequiredService<MismatchedPolicy>());
        services.AddSingleton(builder.Build());
        services.AddScoped<IDocumentNumberingPolicyResolver, DefinitionsDocumentNumberingPolicyResolver>();

        using var sp = services.BuildServiceProvider();
        var resolver = sp.GetRequiredService<IDocumentNumberingPolicyResolver>();

        var act = () => resolver.Resolve("doc.test");
        var ex = act.Should().Throw<DocumentPolicyConfigurationException>()
            .WithMessage("Invalid numbering policy configuration for document type 'doc.test':*")
            .Which;
        ex.ErrorCode.Should().Be(DocumentPolicyConfigurationException.ErrorCodeConst);
        ex.PolicyKind.Should().Be("numbering");
        ex.DocumentTypeCode.Should().Be("doc.test");
        ex.PolicyType.Should().Be(typeof(MismatchedPolicy));
        ex.Reason.Should().Contain("TypeCode does not match");
    }

    private sealed class TestNumberingPolicy : IDocumentNumberingPolicy
    {
        public string TypeCode => "doc.test";
        public bool EnsureNumberOnCreateDraft => true;
        public bool EnsureNumberOnPost => false;
    }

    private sealed class MismatchedPolicy : IDocumentNumberingPolicy
    {
        public string TypeCode => "other";
        public bool EnsureNumberOnCreateDraft => false;
        public bool EnsureNumberOnPost => true;
    }

    private sealed class NotAPolicy;

    private static DefinitionsRegistry DefinitionsWithPolicy<TPolicy>()
        where TPolicy : class, IDocumentNumberingPolicy
    {
        var builder = new DefinitionsBuilder();
        builder.AddDocument("doc.test", definition =>
        {
            definition.Metadata(new DocumentTypeMetadata("doc.test", []));
            definition.NumberingPolicy<TPolicy>();
        });
        return builder.Build();
    }
}
