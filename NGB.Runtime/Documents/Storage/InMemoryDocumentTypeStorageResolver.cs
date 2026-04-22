using NGB.Persistence.Documents.Storage;

namespace NGB.Runtime.Documents.Storage;

/// <summary>
/// Simple resolver over a set of registered per-type storages.
/// Industry modules should register their <see cref="IDocumentTypeStorage"/> implementations.
/// </summary>
public sealed class InMemoryDocumentTypeStorageResolver : IDocumentTypeStorageResolver
{
    private readonly Dictionary<string, IDocumentTypeStorage> _byCode;

    public InMemoryDocumentTypeStorageResolver(IEnumerable<IDocumentTypeStorage> storages)
    {
        _byCode = storages.ToDictionary(
            x => x.TypeCode,
            x => x,
            StringComparer.OrdinalIgnoreCase);
    }

    public IDocumentTypeStorage? TryResolve(string typeCode)
        => _byCode.GetValueOrDefault(typeCode);
}
