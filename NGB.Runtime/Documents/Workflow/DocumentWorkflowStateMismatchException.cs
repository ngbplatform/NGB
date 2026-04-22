using NGB.Tools.Exceptions;

namespace NGB.Runtime.Documents.Workflow;

/// <summary>
/// Thrown when a workflow operation expects a document to be in a specific state,
/// but it is currently in a different one.
/// </summary>
public sealed class DocumentWorkflowStateMismatchException(
    string operation,
    Guid documentId,
    string expectedState,
    string actualState)
    : NgbConflictException(message: $"Expected {expectedState} state, got {actualState}.",
        errorCode: ErrorCodeConst,
        context: new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["operation"] = operation,
            ["documentId"] = documentId,
            ["expectedState"] = expectedState,
            ["actualState"] = actualState,
        })
{
    public const string ErrorCodeConst = "doc.workflow.state_mismatch";

    public string Operation { get; } = operation;

    public Guid DocumentId { get; } = documentId;

    public string ExpectedState { get; } = expectedState;

    public string ActualState { get; } = actualState;
}
