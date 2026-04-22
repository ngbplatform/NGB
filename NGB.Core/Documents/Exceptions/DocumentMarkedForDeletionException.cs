using NGB.Tools.Exceptions;

namespace NGB.Core.Documents.Exceptions;

public sealed class DocumentMarkedForDeletionException(
    string operation,
    Guid documentId,
    DateTime markedForDeletionAtUtc)
    : NgbConflictException(
        message: "Document is marked for deletion.",
        errorCode: ErrorCodeConst,
        context: new Dictionary<string, object?>
        {
            ["operation"] = operation,
            ["documentId"] = documentId,
            ["markedForDeletionAtUtc"] = markedForDeletionAtUtc,
        })
{
    public const string ErrorCodeConst = "doc.marked_for_deletion";

    public string Operation { get; } = operation;

    public Guid DocumentId { get; } = documentId;

    public DateTime MarkedForDeletionAtUtc { get; } = markedForDeletionAtUtc;
}
