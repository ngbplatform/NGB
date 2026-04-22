using NGB.Metadata.Documents.Hybrid;
using NGB.Tools.Exceptions;

namespace NGB.Metadata.Documents.Storage;

/// <summary>
/// Registry snapshot. Populate it at composition root (DI) in your host/industry module.
/// </summary>
public sealed class DocumentTypeRegistry : IDocumentTypeRegistry
{
    private readonly Dictionary<string, DocumentTypeMetadata> _byCode = new(StringComparer.OrdinalIgnoreCase);

    public DocumentTypeRegistry(IEnumerable<DocumentTypeMetadata>? initial = null)
    {
        if (initial is null)
            return;

        foreach (var t in initial)
        {
            Register(t);
        }
    }

    public DocumentTypeMetadata? TryGet(string typeCode) => _byCode.GetValueOrDefault(typeCode);

    public IReadOnlyCollection<DocumentTypeMetadata> GetAll() => _byCode.Values.ToArray();
    
    public void Register(DocumentTypeMetadata metadata)
    {
        if (metadata is null)
            throw new NgbArgumentRequiredException(nameof(metadata));

        if (string.IsNullOrWhiteSpace(metadata.TypeCode))
            throw new NgbArgumentInvalidException(nameof(metadata), "TypeCode is required.");

        // Fail fast on duplicates (case-insensitive). Overwriting silently is dangerous because
        // schema validation and generic tooling become non-deterministic.
        if (_byCode.TryGetValue(metadata.TypeCode, out var existing))
        {
            // Allow idempotent re-registration of the exact same metadata.
            if (Equals(existing, metadata))
                return;

            throw new NgbConfigurationViolationException(
                message: $"Document type metadata for '{metadata.TypeCode}' is already registered.",
                context: new Dictionary<string, object?>
                {
                    ["typeCode"] = metadata.TypeCode,
                });
        }

        _byCode.Add(metadata.TypeCode, metadata);
    }
}
