namespace NGB.Runtime.Documents.Workflow;

/// <summary>
/// Canonical helper to execute document workflows with consistent:
/// - transaction semantics (optionally owned by the executor)
/// - advisory document lock is provided)
/// - lifecycle logs (Started / Completed / NoOp)
/// </summary>
public interface IDocumentWorkflowExecutor
{
    /// <summary>
    /// Executes a workflow action.
    /// The action should return <c>true</c> if the workflow performed changes, otherwise <c>false</c> (idempotent no-op).
    /// </summary>
    Task ExecuteAsync(
        string operationName,
        Guid? documentId,
        Func<CancellationToken, Task<bool>> action,
        bool manageTransaction = true,
        CancellationToken ct = default);

    /// <summary>
    /// Convenience overload for workflows that always have a document id.
    /// </summary>
    Task ExecuteAsync(
        Guid documentId,
        string operationName,
        Func<CancellationToken, Task<bool>> action,
        bool manageTransaction = true,
        CancellationToken ct = default)
        => ExecuteAsync(operationName, documentId, action, manageTransaction, ct);
}
