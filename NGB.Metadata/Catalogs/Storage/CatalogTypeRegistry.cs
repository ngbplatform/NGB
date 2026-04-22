using NGB.Core.Catalogs.Exceptions;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Tools.Exceptions;

namespace NGB.Metadata.Catalogs.Storage;

public sealed class CatalogTypeRegistry : ICatalogTypeRegistry
{
    private readonly Dictionary<string, CatalogTypeMetadata> _byCode = new(StringComparer.OrdinalIgnoreCase);

    public void Register(CatalogTypeMetadata metadata)
    {
        if (metadata is null)
            throw new NgbArgumentRequiredException(nameof(metadata));
        
        if (string.IsNullOrWhiteSpace(metadata.CatalogCode))
            throw new NgbArgumentInvalidException(nameof(metadata), "CatalogCode is required.");

        // Fail fast on duplicates (case-insensitive). Overwriting silently is dangerous because
        // schema validation and generic tooling become non-deterministic.
        if (_byCode.TryGetValue(metadata.CatalogCode, out var existing))
        {
            // Allow idempotent re-registration of the exact same metadata.
            if (Equals(existing, metadata))
                return;

            throw new NgbConfigurationViolationException(
                message: $"Catalog metadata for '{metadata.CatalogCode}' is already registered.",
                context: new Dictionary<string, object?>
                {
                    ["catalogCode"] = metadata.CatalogCode,
                });
        }

        _byCode.Add(metadata.CatalogCode, metadata);
    }

    public CatalogTypeMetadata GetRequired(string catalogCode)
    {
        return !_byCode.TryGetValue(catalogCode, out var m)
            ? throw new CatalogTypeNotFoundException(catalogCode)
            : m;
    }

    public bool TryGet(string catalogCode, out CatalogTypeMetadata? metadata)
        => _byCode.TryGetValue(catalogCode, out metadata);

    public IReadOnlyCollection<CatalogTypeMetadata> All() => _byCode.Values.ToArray();
}
