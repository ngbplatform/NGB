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
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Metadata.Documents.Hybrid;
using NGB.Persistence.Catalogs.Storage;
using NGB.Persistence.Documents.Storage;
using NGB.Runtime.Definitions.Validation;
using NGB.Runtime.DependencyInjection;
using NGB.PostgreSql.DependencyInjection;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

public sealed class Definitions_StartupValidation_FailsFast_BindingsAndMetadata_P0Tests(PostgresTestFixture fixture)
    : IClassFixture<PostgresTestFixture>
{
    [Fact]
    public async Task StartAsync_WhenBindingsAndMetadataAreInvalid_FailsFastWithAllExpectedErrors()
    {
        using var host = CreateHost(new BadBindingsAndMetadataContributor());

        var act = async () => await host.StartAsync(CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DefinitionsValidationException>();

        ex.Which.Errors.Should().Contain(e => e.Contains("Document 'it_def_bind.doc': Metadata.TypeCode does not match", StringComparison.Ordinal));
        ex.Which.Errors.Should().Contain(e => e.Contains("Document 'it_def_bind.doc': TypedStorageType must be a concrete type", StringComparison.Ordinal));
        ex.Which.Errors.Should().Contain(e => e.Contains("Document 'it_def_bind.doc': PostingHandlerType must be a concrete type", StringComparison.Ordinal));
        ex.Which.Errors.Should().Contain(e => e.Contains("Document 'it_def_bind.doc': NumberingPolicyType must be a closed constructed type", StringComparison.Ordinal));
        ex.Which.Errors.Should().Contain(e => e.Contains("Document 'it_def_bind.doc': ApprovalPolicyType must implement IDocumentApprovalPolicy", StringComparison.Ordinal));
        ex.Which.Errors.Should().Contain(e => e.Contains("Document 'it_def_bind.doc': DraftValidatorTypes must implement IDocumentDraftValidator", StringComparison.Ordinal));
        ex.Which.Errors.Should().Contain(e => e.Contains("Document 'it_def_bind.doc': PostValidatorTypes must be a concrete type", StringComparison.Ordinal));

        ex.Which.Errors.Should().Contain(e => e.Contains("Catalog 'it_def_bind.cat': Metadata.CatalogCode does not match", StringComparison.Ordinal));
        ex.Which.Errors.Should().Contain(e => e.Contains("Catalog 'it_def_bind.cat': TypedStorageType must be a concrete type", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StartAsync_WhenConcreteBindingIsNotRegisteredInDI_FailsFastWithExpectedError()
    {
        using var host = CreateHost(new MissingDiRegistrationContributor());

        var act = async () => await host.StartAsync(CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DefinitionsValidationException>();

        ex.Which.Errors.Should().ContainSingle(e =>
            e.Contains("Document 'it_def_di.doc': TypedStorageType", StringComparison.Ordinal)
            && e.Contains("not registered in DI", StringComparison.Ordinal));
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

    private sealed class BadBindingsAndMetadataContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument(
                typeCode: "it_def_bind.doc",
                configure: d => d
                    // Metadata.TypeCode mismatch
                    .Metadata(new DocumentTypeMetadata(
                        "it_def_bind.doc_WRONG",
                        Array.Empty<DocumentTableMetadata>(),
                        new DocumentPresentationMetadata("IT Bad Bindings Doc"),
                        new DocumentMetadataVersion(1, "it-tests")))
                    // Binding shape and interface checks
                    .TypedStorage(typeof(IDocumentTypeStorage)) // interface
                    .PostingHandler(typeof(AbstractPostingHandler)) // abstract
                    .NumberingPolicy(typeof(OpenGenericNumberingPolicy<>)) // open generic
                    .ApprovalPolicy(typeof(string)) // wrong interface
                    .AddDraftValidator(typeof(string)) // wrong interface
                    .AddPostValidator(typeof(IDocumentPostValidator))); // interface

            builder.AddCatalog(
                typeCode: "it_def_bind.cat",
                configure: c => c
                    .Metadata(new CatalogTypeMetadata(
                        "it_def_bind.cat_WRONG",
                        "IT Bad Bindings Catalog",
                        Array.Empty<CatalogTableMetadata>(),
                        new CatalogPresentationMetadata("cat_it_def_bind.cat", "name"),
                        new CatalogMetadataVersion(1, "it-tests")))
                    .TypedStorage(typeof(ICatalogTypeStorage))); // interface
        }

        private abstract class AbstractPostingHandler : IDocumentPostingHandler
        {
            public string TypeCode => "it_def_bind.doc";
            public abstract Task BuildEntriesAsync(DocumentRecord document, IAccountingPostingContext ctx, CancellationToken ct);
        }

        private sealed class OpenGenericNumberingPolicy<T> : IDocumentNumberingPolicy
        {
            public string TypeCode => "it_def_bind.doc";
            public bool EnsureNumberOnCreateDraft => false;
            public bool EnsureNumberOnPost => false;
        }
    }

    private sealed class MissingDiRegistrationContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument(
                typeCode: "it_def_di.doc",
                configure: d => d
                    .Metadata(new DocumentTypeMetadata(
                        "it_def_di.doc",
                        Array.Empty<DocumentTableMetadata>(),
                        new DocumentPresentationMetadata("IT Missing DI Registration"),
                        new DocumentMetadataVersion(1, "it-tests")))
                    .TypedStorage(typeof(ValidButNotRegisteredDocStorage)));
        }

        private sealed class ValidButNotRegisteredDocStorage : IDocumentTypeStorage
        {
            public string TypeCode => "it_def_di.doc";
            public Task CreateDraftAsync(Guid documentId, CancellationToken ct = default) => Task.CompletedTask;
            public Task DeleteDraftAsync(Guid documentId, CancellationToken ct = default) => Task.CompletedTask;
        }
    }
}
