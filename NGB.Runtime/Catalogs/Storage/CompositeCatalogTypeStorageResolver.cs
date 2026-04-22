using NGB.Definitions;
using NGB.Core.Catalogs.Exceptions;
using NGB.Persistence.Catalogs.Storage;
using NGB.Runtime.Internal;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Catalogs.Storage;

/// <summary>
/// Resolver that prefers the TypedStorage binding declared in Definitions, and falls back to
/// resolving storages by their <see cref="ICatalogTypeStorage.CatalogCode"/>.
/// </summary>
public sealed class CompositeCatalogTypeStorageResolver(
    DefinitionsRegistry definitions,
    IEnumerable<ICatalogTypeStorage> storages)
    : ICatalogTypeStorageResolver
{
    private readonly DefinitionsRegistry _definitions = definitions ?? throw new NgbArgumentRequiredException(nameof(definitions));
    private readonly IReadOnlyList<ICatalogTypeStorage> _allStorages = DefinitionRuntimeBindingHelpers.ToReadOnlyList(
        storages ?? throw new NgbArgumentRequiredException(nameof(storages)));
    private readonly Dictionary<string, ICatalogTypeStorage> _boundStorages = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _boundStoragesGate = new();
    private readonly InMemoryCatalogTypeStorageResolver _fallback = new(
        BuildFallbackStorages(
            definitions ?? throw new NgbArgumentRequiredException(nameof(definitions)),
            DefinitionRuntimeBindingHelpers.ToReadOnlyList(storages ?? throw new NgbArgumentRequiredException(nameof(storages)))));

    public ICatalogTypeStorage? TryResolve(string catalogCode)
    {
        if (string.IsNullOrWhiteSpace(catalogCode))
            throw new NgbArgumentRequiredException(nameof(catalogCode));

        if (_definitions.TryGetCatalog(catalogCode, out var def) && def.TypedStorageType is not null)
            return ResolveBoundStorage(def);

        return _fallback.TryResolve(catalogCode);
    }

    private ICatalogTypeStorage ResolveBoundStorage(NGB.Definitions.Catalogs.CatalogTypeDefinition def)
    {
        if (_boundStorages.TryGetValue(def.TypeCode, out var cached))
            return cached;

        lock (_boundStoragesGate)
        {
            if (_boundStorages.TryGetValue(def.TypeCode, out cached))
                return cached;

            var resolved = BuildBoundStorage(def);
            _boundStorages[def.TypeCode] = resolved;
            return resolved;
        }
    }

    private ICatalogTypeStorage BuildBoundStorage(NGB.Definitions.Catalogs.CatalogTypeDefinition def)
    {
        var storageType = def.TypedStorageType
            ?? throw new NgbInvariantViolationException($"Catalog '{def.TypeCode}' has no typed storage binding.");

        if (!typeof(ICatalogTypeStorage).IsAssignableFrom(storageType))
        {
            throw new CatalogTypedStorageMisconfiguredException(
                def.TypeCode,
                reason: "typed_storage_must_implement_contract",
                details: new { storageType = storageType.FullName });
        }

        var matches = DefinitionRuntimeBindingHelpers.FindMatches(storageType, _allStorages);

        if (matches.Length == 0)
        {
            throw new CatalogTypedStorageMisconfiguredException(
                def.TypeCode,
                reason: "typed_storage_not_registered_in_di",
                details: new { storageType = storageType.FullName });
        }

        if (matches.Length > 1)
        {
            throw new CatalogTypedStorageMisconfiguredException(
                def.TypeCode,
                reason: "typed_storage_multiple_matches",
                details: new
                {
                    storageType = storageType.FullName,
                    matches = matches.Select(storage => storage.GetType().FullName ?? storage.GetType().Name).ToArray()
                });
        }

        var storage = matches[0];
        if (!string.Equals(storage.CatalogCode, def.TypeCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new CatalogTypedStorageMisconfiguredException(
                def.TypeCode,
                reason: "typed_storage_catalog_code_mismatch",
                details: new { storageType = storageType.FullName, storageCatalogCode = storage.CatalogCode });
        }

        return storage;
    }

    private static IReadOnlyList<ICatalogTypeStorage> BuildFallbackStorages(
        DefinitionsRegistry definitions,
        IEnumerable<ICatalogTypeStorage> storages)
    {
        var boundCodes = definitions.Catalogs
            .Where(def => def.TypedStorageType is not null)
            .Select(def => def.TypeCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var fallback = new Dictionary<string, ICatalogTypeStorage>(StringComparer.OrdinalIgnoreCase);
        foreach (var storage in storages)
        {
            if (boundCodes.Contains(storage.CatalogCode))
                continue;

            if (!fallback.TryAdd(storage.CatalogCode, storage))
            {
                throw new CatalogTypedStorageMisconfiguredException(
                    storage.CatalogCode,
                    reason: "typed_storage_duplicate_catalog_code",
                    details: new
                    {
                        firstStorageType = fallback[storage.CatalogCode].GetType().FullName,
                        secondStorageType = storage.GetType().FullName
                    });
            }
        }

        return fallback.Values.ToArray();
    }
}
