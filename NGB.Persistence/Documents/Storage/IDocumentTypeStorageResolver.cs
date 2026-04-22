namespace NGB.Persistence.Documents.Storage;

public interface IDocumentTypeStorageResolver
{
    /// <summary>
    /// Returns storage for the given type code, or null if the type does not have per-type storage in the current solution.
    /// </summary>
    IDocumentTypeStorage? TryResolve(string typeCode);
}
