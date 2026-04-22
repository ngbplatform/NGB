using NGB.Tools.Exceptions;

namespace NGB.OperationalRegisters.Exceptions;

public sealed class OperationalRegisterDimensionRulesAppendOnlyViolationException(
    Guid registerId,
    string reason,
    IReadOnlyDictionary<string, object?>? details = null)
    : NgbConflictException(
        message: "Operational register dimension rules are append-only after movements exist.",
        errorCode: Code,
        context: OperationalRegisterExceptionContext.Build(registerId, reason, details))
{
    public const string Code = "opreg.dimension_rules.append_only_violation";

    public Guid RegisterId { get; } = registerId;

    public string Reason { get; } = reason;
}
