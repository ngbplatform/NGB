using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Documents;
using NGB.Definitions;
using NGB.Definitions.Documents.Approval;
using NGB.Metadata.Documents.Hybrid;
using NGB.Runtime.Documents.Policies;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Documents.Policies;

public sealed class DefinitionsDocumentApprovalPolicyResolverTests
{
    [Fact]
    public void Constructor_when_required_dependency_is_null_throws()
    {
        var definitions = new DefinitionsBuilder().Build();

        Action nullDefinitions = () => new DefinitionsDocumentApprovalPolicyResolver(
            null!,
            Array.Empty<IDocumentApprovalPolicy>());
        Action nullPolicies = () => new DefinitionsDocumentApprovalPolicyResolver(definitions, null!);

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
        services.AddScoped<IDocumentApprovalPolicyResolver, DefinitionsDocumentApprovalPolicyResolver>();

        using var sp = services.BuildServiceProvider();
        var resolver = sp.GetRequiredService<IDocumentApprovalPolicyResolver>();

        resolver.Resolve("doc.missing").Should().BeNull();
    }

    [Fact]
    public void Resolve_returns_null_when_policy_type_not_set()
    {
        var builder = new DefinitionsBuilder();
        builder.AddDocument("doc.test", d => d.Metadata(new DocumentTypeMetadata("doc.test", Array.Empty<DocumentTableMetadata>())));

        var services = new ServiceCollection();
        services.AddSingleton(builder.Build());
        services.AddScoped<IDocumentApprovalPolicyResolver, DefinitionsDocumentApprovalPolicyResolver>();

        using var sp = services.BuildServiceProvider();
        var resolver = sp.GetRequiredService<IDocumentApprovalPolicyResolver>();

        resolver.Resolve("doc.test").Should().BeNull();
    }

    [Fact]
    public void Resolve_returns_policy_from_definition_and_DI()
    {
        var builder = new DefinitionsBuilder();
        builder.AddDocument("doc.test", d =>
        {
            d.Metadata(new DocumentTypeMetadata("doc.test", Array.Empty<DocumentTableMetadata>()));
            d.ApprovalPolicy<TestApprovalPolicy>();
        });

        var services = new ServiceCollection();
        services.AddSingleton<TestApprovalPolicy>();
        services.AddSingleton<IDocumentApprovalPolicy>(sp => sp.GetRequiredService<TestApprovalPolicy>());
        services.AddSingleton(builder.Build());
        services.AddScoped<IDocumentApprovalPolicyResolver, DefinitionsDocumentApprovalPolicyResolver>();

        using var sp = services.BuildServiceProvider();
        var resolver = sp.GetRequiredService<IDocumentApprovalPolicyResolver>();

        var policy = resolver.Resolve("doc.test");
        policy.Should().NotBeNull();
        policy!.TypeCode.Should().Be("doc.test");
        resolver.Resolve("DOC.TEST").Should().BeSameAs(policy);
    }

    [Fact]
    public void Resolve_when_policy_is_not_registered_throws()
    {
        var resolver = new DefinitionsDocumentApprovalPolicyResolver(
            DefinitionsWithPolicy<TestApprovalPolicy>(),
            Array.Empty<IDocumentApprovalPolicy>());

        Action action = () => resolver.Resolve("doc.test");

        action.Should().Throw<DocumentPolicyConfigurationException>()
            .Which.Reason.Should().Contain("not registered");
    }

    [Fact]
    public void Resolve_when_policy_has_duplicate_registrations_throws()
    {
        var resolver = new DefinitionsDocumentApprovalPolicyResolver(
            DefinitionsWithPolicy<TestApprovalPolicy>(),
            new IDocumentApprovalPolicy[] { new TestApprovalPolicy(), new TestApprovalPolicy() });

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
            d.ApprovalPolicy<NotAPolicy>();
        });

        var services = new ServiceCollection();
        services.AddSingleton<NotAPolicy>();
        services.AddSingleton(builder.Build());
        services.AddScoped<IDocumentApprovalPolicyResolver, DefinitionsDocumentApprovalPolicyResolver>();

        using var sp = services.BuildServiceProvider();
        var resolver = sp.GetRequiredService<IDocumentApprovalPolicyResolver>();

        var act = () => resolver.Resolve("doc.test");
        var ex = act.Should().Throw<DocumentPolicyConfigurationException>()
            .WithMessage("Invalid approval policy configuration for document type 'doc.test':*")
            .Which;
        ex.ErrorCode.Should().Be(DocumentPolicyConfigurationException.ErrorCodeConst);
        ex.PolicyKind.Should().Be("approval");
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
            d.ApprovalPolicy<MismatchedPolicy>();
        });

        var services = new ServiceCollection();
        services.AddSingleton<MismatchedPolicy>();
        services.AddSingleton<IDocumentApprovalPolicy>(sp => sp.GetRequiredService<MismatchedPolicy>());
        services.AddSingleton(builder.Build());
        services.AddScoped<IDocumentApprovalPolicyResolver, DefinitionsDocumentApprovalPolicyResolver>();

        using var sp = services.BuildServiceProvider();
        var resolver = sp.GetRequiredService<IDocumentApprovalPolicyResolver>();

        var act = () => resolver.Resolve("doc.test");
        var ex = act.Should().Throw<DocumentPolicyConfigurationException>()
            .WithMessage("Invalid approval policy configuration for document type 'doc.test':*")
            .Which;
        ex.ErrorCode.Should().Be(DocumentPolicyConfigurationException.ErrorCodeConst);
        ex.PolicyKind.Should().Be("approval");
        ex.DocumentTypeCode.Should().Be("doc.test");
        ex.PolicyType.Should().Be(typeof(MismatchedPolicy));
        ex.Reason.Should().Contain("TypeCode does not match");
    }

    private sealed class TestApprovalPolicy : IDocumentApprovalPolicy
    {
        public string TypeCode => "doc.test";

        public Task EnsureCanPostAsync(DocumentRecord documentForUpdate, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class MismatchedPolicy : IDocumentApprovalPolicy
    {
        public string TypeCode => "other";

        public Task EnsureCanPostAsync(DocumentRecord documentForUpdate, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class NotAPolicy;

    private static DefinitionsRegistry DefinitionsWithPolicy<TPolicy>()
        where TPolicy : class, IDocumentApprovalPolicy
    {
        var builder = new DefinitionsBuilder();
        builder.AddDocument("doc.test", definition =>
        {
            definition.Metadata(new DocumentTypeMetadata("doc.test", []));
            definition.ApprovalPolicy<TPolicy>();
        });
        return builder.Build();
    }
}
