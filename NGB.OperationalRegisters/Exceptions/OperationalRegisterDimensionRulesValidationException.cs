using NGB.Tools.Exceptions;

namespace NGB.OperationalRegisters.Exceptions;

public sealed class OperationalRegisterDimensionRulesValidationException(
    Guid registerId,
    string reason,
    IReadOnlyDictionary<string, object?>? details = null)
    : NgbValidationException(
        message: "Operational register dimension rules validation failed.",
        errorCode: Code,
        context: OperationalRegisterExceptionContext.Build(registerId, reason, details))
{
    public const string Code = "opreg.dimension_rules.validation";

    public Guid RegisterId { get; } = registerId;

    public string Reason { get; } = reason;
}
