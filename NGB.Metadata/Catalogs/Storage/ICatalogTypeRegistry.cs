using NGB.Metadata.Catalogs.Hybrid;

namespace NGB.Metadata.Catalogs.Storage;

public interface ICatalogTypeRegistry
{
    void Register(CatalogTypeMetadata metadata);

    CatalogTypeMetadata GetRequired(string catalogCode);

    bool TryGet(string catalogCode, out CatalogTypeMetadata? metadata);

    IReadOnlyCollection<CatalogTypeMetadata> All();
}
