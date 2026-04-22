namespace NGB.Tools.Exceptions;

/// <summary>
/// Safety-net wrapper for unexpected exceptions that escape runtime/business boundaries.
///
/// Policy:
/// - Do NOT leak raw exception messages into <see cref="NgbException.Context"/> (apps may map it to UX / HTTP payloads).
/// - Preserve the original exception as <see cref="Exception.InnerException"/> for diagnostics.
/// - Always include the high-level <see cref="Operation"/> name and the original <c>exceptionType</c>.
/// </summary>
public sealed class NgbUnexpectedException(
    string operation,
    Exception innerException,
    IReadOnlyDictionary<string, object?>? additionalContext = null)
    : NgbInfrastructureException(message: "Unexpected internal error.",
        errorCode: Code,
        context: BuildContext(operation, innerException, additionalContext),
        innerException: innerException)
{
    public const string Code = "ngb.unexpected";

    public string Operation { get; } = operation;

    public string ExceptionType { get; } = innerException.GetType().FullName ?? innerException.GetType().Name;

    private static IReadOnlyDictionary<string, object?> BuildContext(
        string operation,
        Exception innerException,
        IReadOnlyDictionary<string, object?>? additionalContext)
    {
        if (string.IsNullOrWhiteSpace(operation))
            operation = "(unknown)";

        var ctx = new Dictionary<string, object?>(StringComparer.Ordinal);

        if (additionalContext is not null)
        {
            foreach (var kv in additionalContext)
                ctx[kv.Key] = kv.Value;
        }

        ctx["operation"] = operation;
        ctx["exceptionType"] = innerException.GetType().FullName ?? innerException.GetType().Name;

        // Intentionally do NOT include innerException.Message.
        // It can contain secrets (SQL, connection strings, file paths, etc.).

        return ctx;
    }
}
