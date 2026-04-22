using NGB.Tools.Exceptions;

namespace NGB.OperationalRegisters.Exceptions;

public sealed class OperationalRegisterResourcesAppendOnlyViolationException(
    Guid registerId,
    string reason,
    IReadOnlyDictionary<string, object?>? details = null)
    : NgbConflictException(
        message: "Operational register resources are append-only after movements exist; cannot modify them.",
        errorCode: Code,
        context: OperationalRegisterExceptionContext.Build(registerId, reason, details))
{
    public const string Code = "opreg.resources.append_only_violation";

    public Guid RegisterId { get; } = registerId;

    public string Reason { get; } = reason;
}
