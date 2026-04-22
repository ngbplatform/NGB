using NGB.Tools.Exceptions;

namespace NGB.Core.Documents.Exceptions;

public sealed class DocumentNotFoundException(Guid documentId) : NgbNotFoundException(
    message: $"Document '{documentId}' was not found.",
    errorCode: Code,
    context: new Dictionary<string, object?>
    {
        ["documentId"] = documentId
    })
{
    public const string Code = "doc.not_found";

    public Guid DocumentId { get; } = documentId;
}
