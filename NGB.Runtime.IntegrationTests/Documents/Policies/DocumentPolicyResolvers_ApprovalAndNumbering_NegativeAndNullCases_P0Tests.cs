using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Documents;
using NGB.Definitions;
using NGB.Definitions.Documents.Approval;
using NGB.Definitions.Documents.Numbering;
using NGB.Metadata.Documents.Hybrid;
using NGB.Runtime.Documents.Policies;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents.Policies;

/// <summary>
/// Coverage for platform policy resolvers:
/// - null cases (unknown doc type / no policy configured)
/// - negative cases (wrong interface / not registered in DI / TypeCode mismatch)
///
/// Notes:
/// - We intentionally DO NOT start the host (StartAsync) because startup validation would fail-fast
///   for some invalid definitions, and here we want to verify the runtime guards in resolvers.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DocumentPolicyResolvers_ApprovalAndNumbering_NegativeAndNullCases_P0Tests(PostgresTestFixture fixture)
{
    [Fact]
    public async Task ApprovalPolicyResolver_ReturnsNull_WhenDocumentTypeUnknown()
    {
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var resolver = scope.ServiceProvider.GetRequiredService<IDocumentApprovalPolicyResolver>();

        resolver.Resolve("unknown_doc_type").Should().BeNull();
    }

    [Fact]
    public async Task ApprovalPolicyResolver_ReturnsNull_WhenApprovalPolicyNotConfigured()
    {
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var resolver = scope.ServiceProvider.GetRequiredService<IDocumentApprovalPolicyResolver>();

        // Defined by TestDocumentContributor but has no approval policy configured.
        resolver.Resolve("it_alpha").Should().BeNull();
    }

    [Fact]
    public async Task ApprovalPolicyResolver_Throws_WhenPolicyTypeDoesNotImplementInterface()
    {
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString, services =>
        {
            services.AddSingleton<IDefinitionsContributor, ApprovalPolicyWrongInterfaceContributor>();
            services.AddScoped<NotAnApprovalPolicy>();
        });
        await using var scope = host.Services.CreateAsyncScope();

        var resolver = scope.ServiceProvider.GetRequiredService<IDocumentApprovalPolicyResolver>();

        Action act = () => resolver.Resolve("it_appr_bad_iface");

        act.Should().Throw<DocumentPolicyConfigurationException>()
            .WithMessage("*approval policy configuration*must implement*IDocumentApprovalPolicy*");
    }

    [Fact]
    public async Task ApprovalPolicyResolver_Throws_WhenPolicyTypeNotRegisteredInDI()
    {
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString, services =>
        {
            services.AddSingleton<IDefinitionsContributor, ApprovalPolicyNotRegisteredContributor>();
            // Intentionally do NOT register ValidApprovalPolicyNotRegistered in DI.
        });
        await using var scope = host.Services.CreateAsyncScope();

        var resolver = scope.ServiceProvider.GetRequiredService<IDocumentApprovalPolicyResolver>();

        Action act = () => resolver.Resolve("it_appr_not_registered");

        act.Should().Throw<DocumentPolicyConfigurationException>()
            .WithMessage("*approval policy configuration*not registered*DI container*");
    }

    [Fact]
    public async Task ApprovalPolicyResolver_Throws_WhenPolicyTypeCodeMismatch()
    {
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString, services =>
        {
            services.AddSingleton<IDefinitionsContributor, ApprovalPolicyTypeCodeMismatchContributor>();
            services.AddScoped<ApprovalPolicyTypeCodeMismatch>();
        });
        await using var scope = host.Services.CreateAsyncScope();

        var resolver = scope.ServiceProvider.GetRequiredService<IDocumentApprovalPolicyResolver>();

        Action act = () => resolver.Resolve("it_appr_mismatch");

        act.Should().Throw<DocumentPolicyConfigurationException>()
            .WithMessage("*approval policy configuration*TypeCode does not match*Expected*it_appr_mismatch*");
    }

    [Fact]
    public async Task NumberingPolicyResolver_ReturnsNull_WhenDocumentTypeUnknown()
    {
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var resolver = scope.ServiceProvider.GetRequiredService<IDocumentNumberingPolicyResolver>();

        resolver.Resolve("unknown_doc_type").Should().BeNull();
    }

    [Fact]
    public async Task NumberingPolicyResolver_ReturnsNull_WhenNumberingPolicyNotConfigured()
    {
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var resolver = scope.ServiceProvider.GetRequiredService<IDocumentNumberingPolicyResolver>();

        // Defined by TestDocumentContributor but has no numbering policy configured.
        resolver.Resolve("it_beta").Should().BeNull();
    }

    [Fact]
    public async Task NumberingPolicyResolver_Throws_WhenPolicyTypeDoesNotImplementInterface()
    {
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString, services =>
        {
            services.AddSingleton<IDefinitionsContributor, NumberingPolicyWrongInterfaceContributor>();
            services.AddScoped<NotANumberingPolicy>();
        });
        await using var scope = host.Services.CreateAsyncScope();

        var resolver = scope.ServiceProvider.GetRequiredService<IDocumentNumberingPolicyResolver>();

        Action act = () => resolver.Resolve("it_num_bad_iface");

        act.Should().Throw<DocumentPolicyConfigurationException>()
            .WithMessage("*numbering policy configuration*must implement*IDocumentNumberingPolicy*");
    }

    [Fact]
    public async Task NumberingPolicyResolver_Throws_WhenPolicyTypeNotRegisteredInDI()
    {
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString, services =>
        {
            services.AddSingleton<IDefinitionsContributor, NumberingPolicyNotRegisteredContributor>();
            // Intentionally do NOT register ValidNumberingPolicyNotRegistered in DI.
        });
        await using var scope = host.Services.CreateAsyncScope();

        var resolver = scope.ServiceProvider.GetRequiredService<IDocumentNumberingPolicyResolver>();

        Action act = () => resolver.Resolve("it_num_not_registered");

        act.Should().Throw<DocumentPolicyConfigurationException>()
            .WithMessage("*numbering policy configuration*not registered*DI container*");
    }

    [Fact]
    public async Task NumberingPolicyResolver_Throws_WhenPolicyTypeCodeMismatch()
    {
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString, services =>
        {
            services.AddSingleton<IDefinitionsContributor, NumberingPolicyTypeCodeMismatchContributor>();
            services.AddScoped<NumberingPolicyTypeCodeMismatch>();
        });
        await using var scope = host.Services.CreateAsyncScope();

        var resolver = scope.ServiceProvider.GetRequiredService<IDocumentNumberingPolicyResolver>();

        Action act = () => resolver.Resolve("it_num_mismatch");

        act.Should().Throw<DocumentPolicyConfigurationException>()
            .WithMessage("*numbering policy configuration*TypeCode does not match*Expected*it_num_mismatch*");
    }

    // -----------------------
    // Contributors
    // -----------------------

    private sealed class ApprovalPolicyWrongInterfaceContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument("it_appr_bad_iface", d => d
                .Metadata(Meta("it_appr_bad_iface", "IT Approval Bad Iface"))
                .ApprovalPolicy(typeof(NotAnApprovalPolicy)));
        }
    }

    private sealed class ApprovalPolicyNotRegisteredContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument("it_appr_not_registered", d => d
                .Metadata(Meta("it_appr_not_registered", "IT Approval Not Registered"))
                .ApprovalPolicy(typeof(ValidApprovalPolicyNotRegistered)));
        }
    }

    private sealed class ApprovalPolicyTypeCodeMismatchContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument("it_appr_mismatch", d => d
                .Metadata(Meta("it_appr_mismatch", "IT Approval TypeCode Mismatch"))
                .ApprovalPolicy(typeof(ApprovalPolicyTypeCodeMismatch)));
        }
    }

    private sealed class NumberingPolicyWrongInterfaceContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument("it_num_bad_iface", d => d
                .Metadata(Meta("it_num_bad_iface", "IT Numbering Bad Iface"))
                .NumberingPolicy(typeof(NotANumberingPolicy)));
        }
    }

    private sealed class NumberingPolicyNotRegisteredContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument("it_num_not_registered", d => d
                .Metadata(Meta("it_num_not_registered", "IT Numbering Not Registered"))
                .NumberingPolicy(typeof(ValidNumberingPolicyNotRegistered)));
        }
    }

    private sealed class NumberingPolicyTypeCodeMismatchContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument("it_num_mismatch", d => d
                .Metadata(Meta("it_num_mismatch", "IT Numbering TypeCode Mismatch"))
                .NumberingPolicy(typeof(NumberingPolicyTypeCodeMismatch)));
        }
    }

    private static DocumentTypeMetadata Meta(string code, string displayName)
        => new(
            TypeCode: code,
            Tables: Array.Empty<DocumentTableMetadata>(),
            Presentation: new DocumentPresentationMetadata(displayName),
            Version: new DocumentMetadataVersion(1, "it-tests"));

    // -----------------------
    // Binding types
    // -----------------------

    private sealed class NotAnApprovalPolicy
    {
        public string TypeCode => "it_appr_bad_iface";
    }

    private sealed class ValidApprovalPolicyNotRegistered : IDocumentApprovalPolicy
    {
        public string TypeCode => "it_appr_not_registered";

        public Task EnsureCanPostAsync(DocumentRecord documentForUpdate, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class ApprovalPolicyTypeCodeMismatch : IDocumentApprovalPolicy
    {
        public string TypeCode => "some_other_type";

        public Task EnsureCanPostAsync(DocumentRecord documentForUpdate, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class NotANumberingPolicy
    {
        public string TypeCode => "it_num_bad_iface";
        public bool EnsureNumberOnCreateDraft => true;
        public bool EnsureNumberOnPost => true;
    }

    private sealed class ValidNumberingPolicyNotRegistered : IDocumentNumberingPolicy
    {
        public string TypeCode => "it_num_not_registered";
        public bool EnsureNumberOnCreateDraft => false;
        public bool EnsureNumberOnPost => false;
    }

    private sealed class NumberingPolicyTypeCodeMismatch : IDocumentNumberingPolicy
    {
        public string TypeCode => "not_it_num_mismatch";
        public bool EnsureNumberOnCreateDraft => false;
        public bool EnsureNumberOnPost => false;
    }
}
