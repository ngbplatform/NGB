using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Posting;
using NGB.Accounting.Posting.Validators;
using NGB.Core.Documents;
using NGB.Definitions;
using NGB.Definitions.Documents.Numbering;
using NGB.Definitions.Documents.Posting;
using NGB.Definitions.Documents.Validation;
using NGB.Metadata.Documents.Hybrid;
using NGB.Persistence.Documents.Storage;
using NGB.PostgreSql.DependencyInjection;
using NGB.Runtime.DependencyInjection;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

/// <summary>
/// Additional fail-fast coverage for Definitions startup validation.
/// These tests focus on edge cases that are easy to regress during refactors:
/// - wrong binding interface
/// - abstract/interface binding types
/// - open generic bindings
/// - bindings not registered in DI
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class Definitions_StartupValidation_FailsFast_MoreCases_P1Tests(PostgresTestFixture fixture)
{
    [Fact]
    public async Task StartAsync_WhenDefinitionBinds_TypedStorageThatDoesNotImplementInterface_FailsFast()
    {
        using var host = CreateHost(fixture.ConnectionString, services =>
        {
            services.AddSingleton<IDefinitionsContributor, WrongTypedStorageInterfaceContributor>();
        });

        var act = () => host.StartAsync(CancellationToken.None);

        await act.Should().ThrowAsync<Exception>()
            .WithMessage("*Definitions validation failed*doc.invalid.storage.interface*TypedStorageType*must implement*IDocumentTypeStorage*");
    }

    [Fact]
    public async Task StartAsync_WhenDefinitionBinds_AbstractPostingHandler_FailsFast()
    {
        using var host = CreateHost(fixture.ConnectionString, services =>
        {
            services.AddSingleton<IDefinitionsContributor, AbstractPostingHandlerContributor>();
        });

        var act = () => host.StartAsync(CancellationToken.None);

        await act.Should().ThrowAsync<Exception>()
            .WithMessage("*Definitions validation failed*doc.invalid.handler.abstract*PostingHandlerType*must be a concrete type*");
    }

    [Fact]
    public async Task StartAsync_WhenDefinitionBinds_OpenGenericDraftValidator_FailsFast()
    {
        using var host = CreateHost(fixture.ConnectionString, services =>
        {
            services.AddSingleton<IDefinitionsContributor, OpenGenericDraftValidatorContributor>();
        });

        var act = () => host.StartAsync(CancellationToken.None);

        await act.Should().ThrowAsync<Exception>()
            .WithMessage("*Definitions validation failed*doc.invalid.validator.open*DraftValidatorTypes*closed constructed type*open generics*");
    }

    [Fact]
    public async Task StartAsync_WhenDefinitionBinds_NumberingPolicy_NotRegisteredInDI_FailsFast()
    {
        using var host = CreateHost(fixture.ConnectionString, services =>
        {
            services.AddSingleton<IDefinitionsContributor, NumberingPolicyNotRegisteredContributor>();
            // IMPORTANT: intentionally do NOT register ValidNumberingPolicyNotRegistered in DI.
        });

        var act = () => host.StartAsync(CancellationToken.None);

        await act.Should().ThrowAsync<Exception>()
            .WithMessage("*Definitions validation failed*doc.invalid.numbering.di*NumberingPolicyType*is not registered in DI*");
    }

    [Fact]
    public async Task StartAsync_WhenDefinitionBinds_PostingHandler_NotRegisteredInDI_FailsFast()
    {
        using var host = CreateHost(fixture.ConnectionString, services =>
        {
            services.AddSingleton<IDefinitionsContributor, PostingHandlerNotRegisteredContributor>();
            // IMPORTANT: intentionally do NOT register ValidPostingHandlerNotRegistered in DI.
        });

        var act = () => host.StartAsync(CancellationToken.None);

        await act.Should().ThrowAsync<Exception>()
            .WithMessage("*Definitions validation failed*doc.invalid.handler.di*PostingHandlerType*is not registered in DI*");
    }

    [Fact]
    public async Task StartAsync_WhenDefinitionBinds_NumberingPolicy_RegisteredOnlyAsConcreteType_FailsFast()
    {
        using var host = CreateHost(fixture.ConnectionString, services =>
        {
            services.AddSingleton<IDefinitionsContributor, NumberingPolicyConcreteOnlyContributor>();
            services.AddScoped<ConcreteOnlyNumberingPolicy>();
        });

        var act = () => host.StartAsync(CancellationToken.None);

        await act.Should().ThrowAsync<Exception>()
            .WithMessage("*Definitions validation failed*doc.invalid.numbering.alias*NumberingPolicyType*is not registered in DI as IDocumentNumberingPolicy*");
    }

    [Fact]
    public async Task StartAsync_WhenDefinitionBinds_PostingHandler_WithTypeCodeMismatch_FailsFast()
    {
        using var host = CreateHost(fixture.ConnectionString, services =>
        {
            services.AddSingleton<IDefinitionsContributor, PostingHandlerTypeCodeMismatchContributor>();
            services.AddScoped<MismatchedTypeCodePostingHandler>();
            services.AddScoped<IDocumentPostingHandler>(sp => sp.GetRequiredService<MismatchedTypeCodePostingHandler>());
        });

        var act = () => host.StartAsync(CancellationToken.None);

        await act.Should().ThrowAsync<Exception>()
            .WithMessage("*Definitions validation failed*doc.invalid.handler.typecode*PostingHandlerType*resolved TypeCode*doc.invalid.handler.typecode.other*does not match definition code*doc.invalid.handler.typecode*");
    }

    private static IHost CreateHost(string connectionString, Action<IServiceCollection> configureTestServices)
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
                services.AddNgbPostgres(connectionString);
                services.AddScoped<IAccountingPostingValidator, BasicAccountingPostingValidator>();

                configureTestServices(services);
            })
            .Build();
    }

    private sealed class WrongTypedStorageInterfaceContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument("doc.invalid.storage.interface", d => d
                .Metadata(new DocumentTypeMetadata(
                    TypeCode: "doc.invalid.storage.interface",
                    Tables: Array.Empty<DocumentTableMetadata>(),
                    Presentation: new DocumentPresentationMetadata("Invalid"),
                    Version: new DocumentMetadataVersion(1, "tests")))
                .TypedStorage(typeof(WrongTypedStorage)));
        }
    }

    private sealed class AbstractPostingHandlerContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument("doc.invalid.handler.abstract", d => d
                .Metadata(new DocumentTypeMetadata(
                    TypeCode: "doc.invalid.handler.abstract",
                    Tables: Array.Empty<DocumentTableMetadata>(),
                    Presentation: new DocumentPresentationMetadata("Invalid"),
                    Version: new DocumentMetadataVersion(1, "tests")))
                .PostingHandler(typeof(AbstractPostingHandler)));
        }
    }

    private sealed class OpenGenericDraftValidatorContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument("doc.invalid.validator.open", d => d
                .Metadata(new DocumentTypeMetadata(
                    TypeCode: "doc.invalid.validator.open",
                    Tables: Array.Empty<DocumentTableMetadata>(),
                    Presentation: new DocumentPresentationMetadata("Invalid"),
                    Version: new DocumentMetadataVersion(1, "tests")))
                .AddDraftValidator(typeof(OpenGenericDraftValidator<>)));
        }
    }

    private sealed class NumberingPolicyNotRegisteredContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument("doc.invalid.numbering.di", d => d
                .Metadata(new DocumentTypeMetadata(
                    TypeCode: "doc.invalid.numbering.di",
                    Tables: Array.Empty<DocumentTableMetadata>(),
                    Presentation: new DocumentPresentationMetadata("Invalid"),
                    Version: new DocumentMetadataVersion(1, "tests")))
                .NumberingPolicy(typeof(ValidNumberingPolicyNotRegistered)));
        }
    }

    private sealed class PostingHandlerNotRegisteredContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument("doc.invalid.handler.di", d => d
                .Metadata(new DocumentTypeMetadata(
                    TypeCode: "doc.invalid.handler.di",
                    Tables: Array.Empty<DocumentTableMetadata>(),
                    Presentation: new DocumentPresentationMetadata("Invalid"),
                    Version: new DocumentMetadataVersion(1, "tests")))
                .PostingHandler(typeof(ValidPostingHandlerNotRegistered)));
        }
    }

    private sealed class NumberingPolicyConcreteOnlyContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument("doc.invalid.numbering.alias", d => d
                .Metadata(new DocumentTypeMetadata(
                    TypeCode: "doc.invalid.numbering.alias",
                    Tables: Array.Empty<DocumentTableMetadata>(),
                    Presentation: new DocumentPresentationMetadata("Invalid"),
                    Version: new DocumentMetadataVersion(1, "tests")))
                .NumberingPolicy(typeof(ConcreteOnlyNumberingPolicy)));
        }
    }

    private sealed class PostingHandlerTypeCodeMismatchContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument("doc.invalid.handler.typecode", d => d
                .Metadata(new DocumentTypeMetadata(
                    TypeCode: "doc.invalid.handler.typecode",
                    Tables: Array.Empty<DocumentTableMetadata>(),
                    Presentation: new DocumentPresentationMetadata("Invalid"),
                    Version: new DocumentMetadataVersion(1, "tests")))
                .PostingHandler(typeof(MismatchedTypeCodePostingHandler)));
        }
    }

    // --- Binding types used by tests (intentionally minimal) ---

    private sealed class WrongTypedStorage
    {
    }

    private abstract class AbstractPostingHandler : IDocumentPostingHandler
    {
        public string TypeCode => "doc.invalid.handler.abstract";

        public Task BuildEntriesAsync(DocumentRecord document, IAccountingPostingContext ctx, CancellationToken ct)
            => Task.CompletedTask;
    }

    private sealed class OpenGenericDraftValidator<T> : IDocumentDraftValidator
    {
        public string TypeCode => "doc.invalid.validator.open";

        public Task ValidateCreateDraftAsync(DocumentRecord draft, CancellationToken ct)
            => Task.CompletedTask;
    }

    private sealed class ValidNumberingPolicyNotRegistered : IDocumentNumberingPolicy
    {
        public string TypeCode => "doc.invalid.numbering.di";
        public bool EnsureNumberOnCreateDraft => false;
        public bool EnsureNumberOnPost => false;
    }

    private sealed class ConcreteOnlyNumberingPolicy : IDocumentNumberingPolicy
    {
        public string TypeCode => "doc.invalid.numbering.alias";
        public bool EnsureNumberOnCreateDraft => false;
        public bool EnsureNumberOnPost => false;
    }

    private sealed class ValidPostingHandlerNotRegistered : IDocumentPostingHandler
    {
        public string TypeCode => "doc.invalid.handler.di";

        public Task BuildEntriesAsync(DocumentRecord document, IAccountingPostingContext ctx, CancellationToken ct)
            => Task.CompletedTask;
    }

    private sealed class MismatchedTypeCodePostingHandler : IDocumentPostingHandler
    {
        public string TypeCode => "doc.invalid.handler.typecode.other";

        public Task BuildEntriesAsync(DocumentRecord document, IAccountingPostingContext ctx, CancellationToken ct)
            => Task.CompletedTask;
    }

    // Ensure the file has a reference to IDocumentTypeStorage assembly (not required at runtime here,
    // but helps prevent accidental test-only dependency drift in csproj trimming).
    // ReSharper disable once UnusedMember.Local
    private static Type TouchDocumentTypeStorageAssembly() => typeof(IDocumentTypeStorage);
}
