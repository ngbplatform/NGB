using NGB.Tools.Exceptions;

namespace NGB.ReferenceRegisters.Exceptions;

public sealed class ReferenceRegisterDimensionRulesAppendOnlyViolationException(
    Guid registerId,
    string reason,
    object? details = null)
    : NgbConflictException(
        message: "Reference register dimension rules are append-only after records exist.",
        errorCode: Code,
        context: new Dictionary<string, object?>
        {
            ["registerId"] = registerId,
            ["reason"] = reason,
            ["details"] = details,
        })
{
    public const string Code = "refreg.dimension_rules.append_only_violation";

    public Guid RegisterId { get; } = registerId;
    public string Reason { get; } = reason;
}
