using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Posting.Validators;
using NGB.Definitions;
using NGB.Metadata.Documents.Hybrid;
using NGB.Persistence.Documents.Storage;
using NGB.PostgreSql.DependencyInjection;
using NGB.Runtime.DependencyInjection;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

[Collection(PostgresCollection.Name)]
public sealed class Definitions_StartupValidation_FailsFast_P1Tests(PostgresTestFixture fixture)
{
    [Fact]
    public async Task StartAsync_WithValidDefinitions_DoesNotThrow()
    {
        using var host = Host.CreateDefaultBuilder()
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
            })
            .Build();

        var act = () => host.StartAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StartAsync_WhenDefinitionReferences_UnregisteredTypedStorage_FailsFast()
    {
        using var host = Host.CreateDefaultBuilder()
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

                services.AddSingleton<IDefinitionsContributor, InvalidTypedStorageContributor>();
            })
            .Build();

        var act = () => host.StartAsync(CancellationToken.None);

        await act.Should().ThrowAsync<Exception>()
            .WithMessage("*Definitions validation failed*doc.invalid.storage*TypedStorageType*not registered in DI*");
    }

    [Fact]
    public async Task StartAsync_WhenDefinitionBinds_WrongPostingHandlerInterface_FailsFast()
    {
        using var host = Host.CreateDefaultBuilder()
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

                services.AddSingleton<IDefinitionsContributor, WrongPostingHandlerContributor>();
                services.AddScoped<WrongPostingHandler>();
            })
            .Build();

        var act = () => host.StartAsync(CancellationToken.None);

        await act.Should().ThrowAsync<Exception>()
            .WithMessage("*Definitions validation failed*doc.invalid.handler*PostingHandlerType*must implement*IDocumentPostingHandler*");
    }

    [Fact]
    public async Task StartAsync_WhenDefinitionMetadata_TypeCodeMismatch_FailsFast()
    {
        using var host = Host.CreateDefaultBuilder()
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

                services.AddSingleton<IDefinitionsContributor, MetadataMismatchContributor>();
            })
            .Build();

        var act = () => host.StartAsync(CancellationToken.None);

        await act.Should().ThrowAsync<Exception>()
            .WithMessage("*Definitions validation failed*doc.invalid.meta*Metadata.TypeCode does not match*");
    }

    private sealed class InvalidTypedStorageContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument("doc.invalid.storage", d => d
                .Metadata(new DocumentTypeMetadata(
                    TypeCode: "doc.invalid.storage",
                    Tables: Array.Empty<DocumentTableMetadata>(),
                    Presentation: new DocumentPresentationMetadata("Invalid"),
                    Version: new DocumentMetadataVersion(1, "tests")))
                .TypedStorage<UnregisteredDocStorage>());
        }
    }

    private sealed class WrongPostingHandlerContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument("doc.invalid.handler", d => d
                .Metadata(new DocumentTypeMetadata(
                    TypeCode: "doc.invalid.handler",
                    Tables: Array.Empty<DocumentTableMetadata>(),
                    Presentation: new DocumentPresentationMetadata("Invalid"),
                    Version: new DocumentMetadataVersion(1, "tests")))
                .PostingHandler<WrongPostingHandler>());
        }
    }

    private sealed class MetadataMismatchContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument("doc.invalid.meta", d => d
                .Metadata(new DocumentTypeMetadata(
                    TypeCode: "doc.invalid.meta.other",
                    Tables: Array.Empty<DocumentTableMetadata>(),
                    Presentation: new DocumentPresentationMetadata("Invalid"),
                    Version: new DocumentMetadataVersion(1, "tests"))));
        }
    }

    private sealed class UnregisteredDocStorage : IDocumentTypeStorage
    {
        public string TypeCode => "doc.invalid.storage";

        public Task CreateDraftAsync(Guid documentId, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteDraftAsync(Guid documentId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class WrongPostingHandler
    {
        public string TypeCode => "doc.invalid.handler";
    }
}
