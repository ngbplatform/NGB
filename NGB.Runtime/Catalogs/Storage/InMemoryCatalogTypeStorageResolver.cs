using NGB.Persistence.Catalogs.Storage;

namespace NGB.Runtime.Catalogs.Storage;

/// <summary>
/// Simple resolver used by Core and demos. Industry modules may provide their own resolver.
/// </summary>
public sealed class InMemoryCatalogTypeStorageResolver(IEnumerable<ICatalogTypeStorage> storages)
    : ICatalogTypeStorageResolver
{
    private readonly Dictionary<string, ICatalogTypeStorage> _byCode = storages.ToDictionary(x => x.CatalogCode, StringComparer.OrdinalIgnoreCase);

    public ICatalogTypeStorage? TryResolve(string catalogCode) => _byCode.GetValueOrDefault(catalogCode);
}
