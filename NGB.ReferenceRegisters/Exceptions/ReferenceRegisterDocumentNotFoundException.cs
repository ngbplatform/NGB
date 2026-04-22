using NGB.Tools.Exceptions;

namespace NGB.ReferenceRegisters.Exceptions;

public sealed class ReferenceRegisterDocumentNotFoundException(Guid documentId) : NgbNotFoundException(
    message: "Document was not found.",
    errorCode: Code,
    context: new Dictionary<string, object?>
    {
        ["documentId"] = documentId
    })
{
    public const string Code = "refreg.document.not_found";

    public Guid DocumentId { get; } = documentId;
}
