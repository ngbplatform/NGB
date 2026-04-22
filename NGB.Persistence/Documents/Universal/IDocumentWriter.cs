namespace NGB.Persistence.Documents.Universal;

public interface IDocumentWriter
{
    Task UpsertHeadAsync(
        DocumentHeadDescriptor head,
        Guid documentId,
        IReadOnlyList<DocumentHeadValue> values,
        CancellationToken ct = default);
}
