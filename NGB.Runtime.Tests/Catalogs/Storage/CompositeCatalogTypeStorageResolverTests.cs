using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Definitions;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Persistence.Catalogs.Storage;
using NGB.Runtime.Catalogs.Storage;
using NGB.Core.Catalogs.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Catalogs.Storage;

public sealed class CompositeCatalogTypeStorageResolverTests
{
    private static CatalogTypeMetadata MinimalMetadata(string catalogCode) =>
        new(
            CatalogCode: catalogCode,
            DisplayName: "Test",
            Tables: new List<CatalogTableMetadata>(),
            Presentation: new CatalogPresentationMetadata("cat", "name"),
            Version: new CatalogMetadataVersion(1, "x"));

    [Fact]
    public void TryResolve_WhenDefinitionBindsTypedStorage_ResolvesByTypeFromDi()
    {
        var builder = new DefinitionsBuilder();
        builder.AddCatalog("CAT", c => c
            .Metadata(MinimalMetadata("CAT"))
            .TypedStorage<FakeCatalogStorage>());

        var defs = builder.Build();

        var services = new ServiceCollection();
        services.AddSingleton(defs);
        services.AddSingleton<FakeCatalogStorage>();
        services.AddSingleton<ICatalogTypeStorage>(sp => sp.GetRequiredService<FakeCatalogStorage>());
        services.AddScoped<ICatalogTypeStorageResolver, CompositeCatalogTypeStorageResolver>();

        var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<ICatalogTypeStorageResolver>();

        var storage = resolver.TryResolve("CAT");
        storage.Should().NotBeNull();
        storage.Should().BeOfType<FakeCatalogStorage>();
    }

    [Fact]
    public void TryResolve_WhenDefinitionDeclaresTypedStorageButNotRegistered_Throws()
    {
        var builder = new DefinitionsBuilder();
        builder.AddCatalog("CAT", c => c
            .Metadata(MinimalMetadata("CAT"))
            .TypedStorage<FakeCatalogStorage>());

        var defs = builder.Build();

        var services = new ServiceCollection();
        services.AddSingleton(defs);
        services.AddScoped<ICatalogTypeStorageResolver, CompositeCatalogTypeStorageResolver>();

        var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<ICatalogTypeStorageResolver>();

        Action act = () =>
        {
            _ = resolver.TryResolve("CAT");
        };

        var thrown = act.Should().Throw<CatalogTypedStorageMisconfiguredException>().Which;
        thrown.ErrorCode.Should().Be(CatalogTypedStorageMisconfiguredException.Code);
        thrown.Context.Should().ContainKeys("catalogCode", "reason");
        thrown.Context["catalogCode"].Should().Be("CAT");
        thrown.Context["reason"].Should().Be("typed_storage_not_registered_in_di");
    }

    [Fact]
    public void TryResolve_WhenDefinitionStorageCodeMismatch_Throws()
    {
        var builder = new DefinitionsBuilder();
        builder.AddCatalog("CAT", c => c
            .Metadata(MinimalMetadata("CAT"))
            .TypedStorage<MismatchCatalogStorage>());

        var defs = builder.Build();

        var services = new ServiceCollection();
        services.AddSingleton(defs);
        services.AddSingleton<MismatchCatalogStorage>();
        services.AddSingleton<ICatalogTypeStorage>(sp => sp.GetRequiredService<MismatchCatalogStorage>());
        services.AddScoped<ICatalogTypeStorageResolver, CompositeCatalogTypeStorageResolver>();

        var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<ICatalogTypeStorageResolver>();

        Action act = () =>
        {
            _ = resolver.TryResolve("CAT");
        };

        var thrown = act.Should().Throw<CatalogTypedStorageMisconfiguredException>().Which;
        thrown.ErrorCode.Should().Be(CatalogTypedStorageMisconfiguredException.Code);
        thrown.Context.Should().ContainKeys("catalogCode", "reason");
        thrown.Context["catalogCode"].Should().Be("CAT");
        thrown.Context["reason"].Should().Be("typed_storage_catalog_code_mismatch");
    }

    [Fact]
    public void TryResolve_WhenDefinitionDoesNotBindTypedStorage_FallsBackToRegisteredStorages()
    {
        var builder = new DefinitionsBuilder();
        builder.AddCatalog("CAT", c => c
            .Metadata(MinimalMetadata("CAT")));

        var defs = builder.Build();

        var services = new ServiceCollection();
        services.AddSingleton(defs);
        services.AddSingleton<FallbackCatalogStorage>();
        services.AddSingleton<ICatalogTypeStorage>(sp => sp.GetRequiredService<FallbackCatalogStorage>());
        services.AddScoped<ICatalogTypeStorageResolver, CompositeCatalogTypeStorageResolver>();

        var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<ICatalogTypeStorageResolver>();

        var storage = resolver.TryResolve("CAT");
        storage.Should().NotBeNull();
        storage.Should().BeOfType<FallbackCatalogStorage>();
    }

    private sealed class FakeCatalogStorage : ICatalogTypeStorage
    {
        public string CatalogCode => "CAT";
        public Task EnsureCreatedAsync(Guid catalogId, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid catalogId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class MismatchCatalogStorage : ICatalogTypeStorage
    {
        public string CatalogCode => "OTHER";
        public Task EnsureCreatedAsync(Guid catalogId, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid catalogId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FallbackCatalogStorage : ICatalogTypeStorage
    {
        public string CatalogCode => "CAT";
        public Task EnsureCreatedAsync(Guid catalogId, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid catalogId, CancellationToken ct = default) => Task.CompletedTask;
    }
}
