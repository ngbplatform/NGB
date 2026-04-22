namespace NGB.Tools.Exceptions;

/// <summary>
/// Stable infrastructure exception surface for timeouts.
///
/// Notes:
/// - Do NOT leak raw exception messages into <see cref="NgbException.Context"/>.
/// - Preserve the original exception in <see cref="Exception.InnerException"/> for diagnostics.
/// </summary>
public sealed class NgbTimeoutException(
    string operation,
    Exception innerException,
    IReadOnlyDictionary<string, object?>? additionalContext = null)
    : NgbInfrastructureException(message: "Operation timed out.",
        errorCode: Code,
        context: BuildContext(operation, innerException, additionalContext),
        innerException: innerException)
{
    public const string Code = "ngb.infra.timeout";

    public string Operation { get; } = string.IsNullOrWhiteSpace(operation) ? "(unknown)" : operation;

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
            {
                ctx[kv.Key] = kv.Value;
            }
        }

        ctx["operation"] = operation;
        ctx["exceptionType"] = innerException.GetType().FullName ?? innerException.GetType().Name;

        return ctx;
    }
}
