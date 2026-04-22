using NGB.Metadata.Documents.Hybrid;

namespace NGB.Metadata.Documents.Storage;

/// <summary>
/// Registry of document types supported by the current solution.
/// Industry modules register their document types here.
/// </summary>
public interface IDocumentTypeRegistry
{
    DocumentTypeMetadata? TryGet(string typeCode);

    IReadOnlyCollection<DocumentTypeMetadata> GetAll();
}
