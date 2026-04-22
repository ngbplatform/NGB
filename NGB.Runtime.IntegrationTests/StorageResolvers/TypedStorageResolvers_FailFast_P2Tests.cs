using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.Catalogs.Storage;
using NGB.Persistence.Documents.Storage;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.StorageResolvers;

/// <summary>
/// P2: Fail-fast behavior for runtime typed-storage resolvers.
///
/// InMemory*StorageResolver builds a dictionary over registered storages by code.
/// Duplicate codes must throw early and deterministically.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TypedStorageResolvers_FailFast_P2Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task DocumentTypeStorageResolver_WhenDuplicateTypeCode_ThrowsFailFast()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<DocStorageA_Duplicate>();
                services.AddScoped<IDocumentTypeStorage>(sp => sp.GetRequiredService<DocStorageA_Duplicate>());

                // Same code, different casing -> should still be treated as duplicate.
                services.AddScoped<DocStorageB_Duplicate>();
                services.AddScoped<IDocumentTypeStorage>(sp => sp.GetRequiredService<DocStorageB_Duplicate>());
            });

        await using var scope = host.Services.CreateAsyncScope();

        var act = () => scope.ServiceProvider.GetRequiredService<IDocumentTypeStorageResolver>();

        var ex = act.Should().Throw<Exception>().Which;
        AssertDuplicateKey(ex);
    }

    [Fact]
    public async Task CatalogTypeStorageResolver_WhenDuplicateCatalogCode_ThrowsFailFast()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<CatStorageA_Duplicate>();
                services.AddScoped<ICatalogTypeStorage>(sp => sp.GetRequiredService<CatStorageA_Duplicate>());

                // Same code, different casing -> should still be treated as duplicate.
                services.AddScoped<CatStorageB_Duplicate>();
                services.AddScoped<ICatalogTypeStorage>(sp => sp.GetRequiredService<CatStorageB_Duplicate>());
            });

        await using var scope = host.Services.CreateAsyncScope();

        var act = () => scope.ServiceProvider.GetRequiredService<ICatalogTypeStorageResolver>();

        var ex = act.Should().Throw<Exception>().Which;
        AssertDuplicateKey(ex);
    }

    [Fact]
    public async Task DocumentTypeStorageResolver_WhenUniqueCodes_AllowsResolve_AndMissingReturnsNull()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<DocStorageA>();
                services.AddScoped<IDocumentTypeStorage>(sp => sp.GetRequiredService<DocStorageA>());

                services.AddScoped<DocStorageB>();
                services.AddScoped<IDocumentTypeStorage>(sp => sp.GetRequiredService<DocStorageB>());
            });

        await using var scope = host.Services.CreateAsyncScope();

        var a = scope.ServiceProvider.GetRequiredService<DocStorageA>();
        var b = scope.ServiceProvider.GetRequiredService<DocStorageB>();
        var resolver = scope.ServiceProvider.GetRequiredService<IDocumentTypeStorageResolver>();

        resolver.TryResolve(DocStorageA.CodeUpper).Should().BeSameAs(a);
        resolver.TryResolve(DocStorageB.CodeLower).Should().BeSameAs(b);

        resolver.TryResolve("missing").Should().BeNull();
    }

    [Fact]
    public async Task CatalogTypeStorageResolver_WhenUniqueCodes_AllowsResolve_AndMissingReturnsNull()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<CatStorageA>();
                services.AddScoped<ICatalogTypeStorage>(sp => sp.GetRequiredService<CatStorageA>());

                services.AddScoped<CatStorageB>();
                services.AddScoped<ICatalogTypeStorage>(sp => sp.GetRequiredService<CatStorageB>());
            });

        await using var scope = host.Services.CreateAsyncScope();

        var a = scope.ServiceProvider.GetRequiredService<CatStorageA>();
        var b = scope.ServiceProvider.GetRequiredService<CatStorageB>();
        var resolver = scope.ServiceProvider.GetRequiredService<ICatalogTypeStorageResolver>();

        resolver.TryResolve(CatStorageA.CodeUpper).Should().BeSameAs(a);
        resolver.TryResolve(CatStorageB.CodeLower).Should().BeSameAs(b);

        resolver.TryResolve("missing").Should().BeNull();
    }

    private static void AssertDuplicateKey(Exception ex)
    {
        // Depending on where the exception is thrown (factory/ctor), DI may propagate directly
        // Depending on where the exception is thrown (factory/ctor), DI may wrap it with an outer exception.
        var root = ex;
        while (root.InnerException is not null)
            root = root.InnerException;

        root.Message.Should().Contain("same key", "duplicate type/catalog codes must fail fast with a deterministic duplicate-key error");
    }

    private sealed class DocStorageA_Duplicate : IDocumentTypeStorage
    {
        public const string Code = "it_doc_dup";
        public string TypeCode => Code;
        public Task CreateDraftAsync(Guid documentId, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteDraftAsync(Guid documentId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class DocStorageB_Duplicate : IDocumentTypeStorage
    {
        public string TypeCode => "IT_DOC_DUP";
        public Task CreateDraftAsync(Guid documentId, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteDraftAsync(Guid documentId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class CatStorageA_Duplicate : ICatalogTypeStorage
    {
        public const string Code = "it_cat_dup";
        public string CatalogCode => Code;
        public Task EnsureCreatedAsync(Guid catalogId, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid catalogId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class CatStorageB_Duplicate : ICatalogTypeStorage
    {
        public string CatalogCode => "IT_CAT_DUP";
        public Task EnsureCreatedAsync(Guid catalogId, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid catalogId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class DocStorageA : IDocumentTypeStorage
    {
        public const string CodeUpper = "IT_DOC_A";
        public string TypeCode => CodeUpper;
        public Task CreateDraftAsync(Guid documentId, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteDraftAsync(Guid documentId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class DocStorageB : IDocumentTypeStorage
    {
        public const string CodeLower = "it_doc_b";
        public string TypeCode => CodeLower;
        public Task CreateDraftAsync(Guid documentId, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteDraftAsync(Guid documentId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class CatStorageA : ICatalogTypeStorage
    {
        public const string CodeUpper = "IT_CAT_A";
        public string CatalogCode => CodeUpper;
        public Task EnsureCreatedAsync(Guid catalogId, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid catalogId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class CatStorageB : ICatalogTypeStorage
    {
        public const string CodeLower = "it_cat_b";
        public string CatalogCode => CodeLower;
        public Task EnsureCreatedAsync(Guid catalogId, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid catalogId, CancellationToken ct = default) => Task.CompletedTask;
    }
}
