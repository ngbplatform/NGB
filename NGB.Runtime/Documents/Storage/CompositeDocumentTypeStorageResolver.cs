using NGB.Definitions;
using NGB.Persistence.Documents.Storage;
using NGB.Runtime.Internal;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Documents.Storage;

/// <summary>
/// Resolver that prefers the TypedStorage binding declared in Definitions, and falls back to
/// resolving storages by their <see cref="IDocumentTypeStorage.TypeCode"/>.
///
/// This keeps existing vertical modules working while enabling a module to bind typed storage
/// in its definition (and, optionally, override the storage in provider modules via Extend*).
/// </summary>
public sealed class CompositeDocumentTypeStorageResolver : IDocumentTypeStorageResolver
{
    private readonly DefinitionsRegistry _definitions;
    private readonly IReadOnlyList<IDocumentTypeStorage> _allStorages;
    private readonly Dictionary<string, IDocumentTypeStorage> _boundStorages = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _boundStoragesGate = new();
    private readonly InMemoryDocumentTypeStorageResolver _fallback;

    public CompositeDocumentTypeStorageResolver(
        DefinitionsRegistry definitions,
        IEnumerable<IDocumentTypeStorage> storages)
    {
        _definitions = definitions ?? throw new NgbArgumentRequiredException(nameof(definitions));
        _allStorages = DefinitionRuntimeBindingHelpers.ToReadOnlyList(
            storages ?? throw new NgbArgumentRequiredException(nameof(storages)));
        _fallback = new InMemoryDocumentTypeStorageResolver(BuildFallbackStorages(_definitions, _allStorages));
    }

    public IDocumentTypeStorage? TryResolve(string typeCode)
    {
        if (string.IsNullOrWhiteSpace(typeCode))
            throw new NgbArgumentRequiredException(nameof(typeCode));

        if (_definitions.TryGetDocument(typeCode, out var def) && def.TypedStorageType is not null)
            return ResolveBoundStorage(def);

        return _fallback.TryResolve(typeCode);
    }

    private IDocumentTypeStorage ResolveBoundStorage(NGB.Definitions.Documents.DocumentTypeDefinition def)
    {
        lock (_boundStoragesGate)
        {
            if (_boundStorages.TryGetValue(def.TypeCode, out var cached))
                return cached;

            var resolved = BuildBoundStorage(def);
            _boundStorages[def.TypeCode] = resolved;
            return resolved;
        }
    }

    private IDocumentTypeStorage BuildBoundStorage(NGB.Definitions.Documents.DocumentTypeDefinition def)
    {
        var storageType = def.TypedStorageType!;

        if (!typeof(IDocumentTypeStorage).IsAssignableFrom(storageType))
        {
            throw new NgbConfigurationViolationException(
                $"Document type '{def.TypeCode}' declares typed storage '{storageType.Name}', but it must implement IDocumentTypeStorage.",
                context: new Dictionary<string, object?>
                {
                    ["typeCode"] = def.TypeCode,
                    ["storageType"] = storageType.FullName
                });
        }

        var matches = DefinitionRuntimeBindingHelpers.FindMatches(storageType, _allStorages);

        if (matches.Length == 0)
        {
            throw new NgbConfigurationViolationException(
                $"Document type '{def.TypeCode}' declares typed storage '{storageType.Name}', but it is not registered in DI.",
                context: new Dictionary<string, object?>
                {
                    ["typeCode"] = def.TypeCode,
                    ["storageType"] = storageType.FullName
                });
        }

        if (matches.Length > 1)
        {
            throw new NgbConfigurationViolationException(
                $"Document type '{def.TypeCode}' declares typed storage '{storageType.Name}', but multiple registrations match that binding.",
                context: new Dictionary<string, object?>
                {
                    ["typeCode"] = def.TypeCode,
                    ["storageType"] = storageType.FullName,
                    ["matches"] = matches.Select(storage => storage.GetType().ToString()).ToArray()
                });
        }

        var storage = matches[0];
        if (!string.Equals(storage.TypeCode, def.TypeCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new NgbConfigurationViolationException(
                $"Typed storage TypeCode '{storage.TypeCode}' does not match document type '{def.TypeCode}'. storage={storageType.Name}",
                context: new Dictionary<string, object?>
                {
                    ["typeCode"] = def.TypeCode,
                    ["storageType"] = storageType.FullName,
                    ["storageTypeCode"] = storage.TypeCode
                });
        }

        return storage;
    }

    private static IReadOnlyList<IDocumentTypeStorage> BuildFallbackStorages(
        DefinitionsRegistry definitions,
        IEnumerable<IDocumentTypeStorage> storages)
    {
        var boundCodes = definitions.Documents
            .Where(def => def.TypedStorageType is not null)
            .Select(def => def.TypeCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var fallback = new Dictionary<string, IDocumentTypeStorage>(StringComparer.OrdinalIgnoreCase);
        foreach (var storage in storages)
        {
            if (boundCodes.Contains(storage.TypeCode))
                continue;

            if (!fallback.TryAdd(storage.TypeCode, storage))
            {
                throw new NgbConfigurationViolationException(
                    $"Multiple typed storages are registered for document type '{storage.TypeCode}' (duplicate same key).",
                    context: new Dictionary<string, object?>
                    {
                        ["typeCode"] = storage.TypeCode,
                        ["firstStorageType"] = fallback[storage.TypeCode].GetType().FullName,
                        ["secondStorageType"] = storage.GetType().FullName
                    });
            }
        }

        return fallback.Values.ToArray();
    }
}
