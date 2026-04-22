namespace NGB.Persistence.Catalogs.Storage;

public interface ICatalogTypeStorageResolver
{
    ICatalogTypeStorage? TryResolve(string catalogCode);
}