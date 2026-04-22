using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.Catalogs.Storage;
using NGB.Persistence.Documents.Storage;

namespace NGB.Runtime.IntegrationTests.Schema;

/// <summary>
/// Test-only helpers for schema validation tests.
/// Schema validators now require per-type storage registrations for types that declare typed tables in metadata.
/// </summary>
internal static class SchemaTestTypedStorages
{
    public static IServiceCollection AddNoopDocumentTypeStorage(this IServiceCollection services, string typeCode)
    {
        services.AddSingleton<IDocumentTypeStorage>(_ => new NoopDocumentTypeStorage(typeCode));
        return services;
    }

    public static IServiceCollection AddNoopCatalogTypeStorage(this IServiceCollection services, string catalogCode)
    {
        services.AddSingleton<ICatalogTypeStorage>(_ => new NoopCatalogTypeStorage(catalogCode));
        return services;
    }

    private sealed class NoopDocumentTypeStorage(string typeCode) : IDocumentTypeStorage
    {
        public string TypeCode { get; } = typeCode;

        public Task CreateDraftAsync(Guid documentId, CancellationToken ct = default) => Task.CompletedTask;

        public Task DeleteDraftAsync(Guid documentId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NoopCatalogTypeStorage(string catalogCode) : ICatalogTypeStorage
    {
        public string CatalogCode { get; } = catalogCode;

        public Task EnsureCreatedAsync(Guid catalogId, CancellationToken ct = default) => Task.CompletedTask;

        public Task DeleteAsync(Guid catalogId, CancellationToken ct = default) => Task.CompletedTask;
    }
}
