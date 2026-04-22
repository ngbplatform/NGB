using NGB.Accounting.PostingState;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Posting;

public sealed class PostingAlreadyInProgressException(Guid documentId, PostingOperation operation)
    : NgbConflictException(
        message: $"Posting is already in progress. documentId={documentId}, operation={operation}",
        errorCode: ErrorCodeConst,
        context: new Dictionary<string, object?>
        {
            ["documentId"] = documentId,
            ["operation"] = operation.ToString()
        })
{
    public const string ErrorCodeConst = "accounting.posting.in_progress";

    public Guid DocumentId { get; } = documentId;
    public PostingOperation Operation { get; } = operation;
}
