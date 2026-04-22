using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Definitions;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Metadata.Documents.Hybrid;
using NGB.Persistence.Catalogs.Storage;
using NGB.Persistence.Documents.Storage;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.StorageResolvers;

/// <summary>
/// P0: Composite*StorageResolver prefers the TypedStorage binding declared in Definitions,
/// and falls back to the in-memory resolver over registered storages.
///
/// This test ensures a module can override an existing fallback storage without triggering
/// the duplicate-type-code fail-fast behavior in the fallback dictionary.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TypedStorageResolvers_DefinitionsOverride_Fallback_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task DocumentTypeStorageResolver_WhenDefinitionBindsTypedStorage_OverridesFallbackStorage()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, TestDocContributor>();

                // Fallback: registered as IDocumentTypeStorage.
                services.AddScoped<FallbackDocStorage>();
                services.AddScoped<IDocumentTypeStorage>(sp => sp.GetRequiredService<FallbackDocStorage>());

                // IMPORTANT: register definition-bound storage only as its concrete type,
                // so it does not appear in IEnumerable<IDocumentTypeStorage> used for fallback resolution.
                services.AddScoped<DefinitionDocStorage>();
            });

        await using var scope = host.Services.CreateAsyncScope();

        var fallback = scope.ServiceProvider.GetRequiredService<FallbackDocStorage>();
        var expected = scope.ServiceProvider.GetRequiredService<DefinitionDocStorage>();
        var resolver = scope.ServiceProvider.GetRequiredService<IDocumentTypeStorageResolver>();

        var resolved = resolver.TryResolve(DefinitionDocStorage.Code);

        resolved.Should().NotBeNull();
        resolved.Should().BeSameAs(expected);
        resolved.Should().NotBeSameAs(fallback);
    }

    [Fact]
    public async Task CatalogTypeStorageResolver_WhenDefinitionBindsTypedStorage_OverridesFallbackStorage()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, TestCatalogContributor>();

                // Fallback: registered as ICatalogTypeStorage.
                services.AddScoped<FallbackCatalogStorage>();
                services.AddScoped<ICatalogTypeStorage>(sp => sp.GetRequiredService<FallbackCatalogStorage>());

                // IMPORTANT: register definition-bound storage only as its concrete type,
                // so it does not appear in IEnumerable<ICatalogTypeStorage> used for fallback resolution.
                services.AddScoped<DefinitionCatalogStorage>();
            });

        await using var scope = host.Services.CreateAsyncScope();

        var fallback = scope.ServiceProvider.GetRequiredService<FallbackCatalogStorage>();
        var expected = scope.ServiceProvider.GetRequiredService<DefinitionCatalogStorage>();
        var resolver = scope.ServiceProvider.GetRequiredService<ICatalogTypeStorageResolver>();

        var resolved = resolver.TryResolve(DefinitionCatalogStorage.Code);

        resolved.Should().NotBeNull();
        resolved.Should().BeSameAs(expected);
        resolved.Should().NotBeSameAs(fallback);
    }

    private sealed class TestDocContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument(
                typeCode: DefinitionDocStorage.Code,
                configure: d => d
                    .Metadata(new DocumentTypeMetadata(
                        TypeCode: DefinitionDocStorage.Code,
                        Tables: Array.Empty<DocumentTableMetadata>(),
                        Presentation: new DocumentPresentationMetadata("Test document"),
                        Version: new DocumentMetadataVersion(1, "tests")))
                    .TypedStorage<DefinitionDocStorage>());
        }
    }

    private sealed class TestCatalogContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddCatalog(
                typeCode: DefinitionCatalogStorage.Code,
                configure: c => c
                    .Metadata(new CatalogTypeMetadata(
                        CatalogCode: DefinitionCatalogStorage.Code,
                        DisplayName: "Test catalog",
                        Tables: Array.Empty<CatalogTableMetadata>(),
                        Presentation: new CatalogPresentationMetadata("cat_test", "name"),
                        Version: new CatalogMetadataVersion(1, "tests")))
                    .TypedStorage<DefinitionCatalogStorage>());
        }
    }

    private sealed class FallbackDocStorage : IDocumentTypeStorage
    {
        public const string Code = "it_doc_def_override";
        public string TypeCode => Code;

        public Task CreateDraftAsync(Guid documentId, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteDraftAsync(Guid documentId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class DefinitionDocStorage : IDocumentTypeStorage
    {
        public const string Code = "it_doc_def_override";
        public string TypeCode => Code;

        public Task CreateDraftAsync(Guid documentId, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteDraftAsync(Guid documentId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FallbackCatalogStorage : ICatalogTypeStorage
    {
        public const string Code = "it_cat_def_override";
        public string CatalogCode => Code;

        public Task EnsureCreatedAsync(Guid catalogId, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid catalogId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class DefinitionCatalogStorage : ICatalogTypeStorage
    {
        public const string Code = "it_cat_def_override";
        public string CatalogCode => Code;

        public Task EnsureCreatedAsync(Guid catalogId, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid catalogId, CancellationToken ct = default) => Task.CompletedTask;
    }
}
