namespace NGB.Tools.Exceptions;

/// <summary>
/// Central policy for exception surfaces.
///
/// Goal: callers that treat NGB as a platform should receive stable, structured
/// <see cref="NgbException"/> errors for unexpected failures, while preserving the
/// original exception in <see cref="Exception.InnerException"/> for diagnostics.
///
/// Notes:
/// - Domain/business code should prefer throwing specific custom exceptions.
/// - This policy is a safety net for "we missed a case" scenarios.
/// </summary>
public static class NgbExceptionPolicy
{
    /// <summary>
    /// Wrap unexpected exceptions into an <see cref="NgbUnexpectedException"/>, while preserving
    /// "expected" exception types used as control-flow / business rule signals.
    ///
    /// Policy:
    /// - <see cref="OperationCanceledException"/> passes through unchanged (normal control flow).
    /// - <see cref="TimeoutException"/> is wrapped into <see cref="NgbTimeoutException"/>.
    /// - Already-structured <see cref="NgbException"/> passes through unchanged.
    /// - Provider <see cref="System.Data.Common.DbException"/> passes through unchanged to preserve provider-specific
    ///   error details (SqlState, constraint names, etc.) without adding provider dependencies at the Core layer.
    /// - Everything else is wrapped into a stable <see cref="NgbException"/> surface.
    /// </summary>
    public static Exception Apply(
        Exception ex,
        string operation,
        IReadOnlyDictionary<string, object?>? additionalContext = null)
    {
        if (ex is null)
            throw new NgbArgumentRequiredException(nameof(ex));

        // Cancellation is part of normal control flow.
        if (ex is OperationCanceledException)
            return ex;

        // Timeouts are an infrastructure concern; keep a stable, structured surface.
        if (ex is TimeoutException)
            return new NgbTimeoutException(operation, ex, additionalContext);

        // Already structured.
        if (ex is NgbException)
            return ex;

        // DB drivers often throw DbException-derived errors (e.g. unique violations) that tests/assertions rely on.
        // Avoid adding provider-specific dependencies (Npgsql) at the Core layer.
        if (ex is System.Data.Common.DbException)
            return ex;

        return new NgbUnexpectedException(operation, ex, additionalContext);
    }
}
