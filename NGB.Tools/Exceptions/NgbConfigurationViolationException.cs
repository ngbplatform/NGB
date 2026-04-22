namespace NGB.Tools.Exceptions;

/// <summary>
/// Strict alternative to <see cref="InvalidOperationException"/> used for configuration-time fail-fast checks.
/// </summary>
public sealed class NgbConfigurationViolationException(
    string message,
    IReadOnlyDictionary<string, object?>? context = null,
    Exception? innerException = null)
    : NgbConfigurationException(message, Code, context, innerException)
{
    public const string Code = "ngb.configuration.violation";
}
