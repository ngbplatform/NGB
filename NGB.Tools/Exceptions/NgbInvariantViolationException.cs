namespace NGB.Tools.Exceptions;

/// <summary>
/// Strict alternative to <see cref="InvalidOperationException"/> for internal invariants.
/// This is a programmer error / impossible state, but still must follow the NGB exception contract.
/// </summary>
public sealed class NgbInvariantViolationException(
    string message,
    IReadOnlyDictionary<string, object?>? context = null,
    Exception? innerException = null)
    : NgbInfrastructureException(message, Code, context, innerException)
{
    public const string Code = "ngb.invariant.violation";
}
