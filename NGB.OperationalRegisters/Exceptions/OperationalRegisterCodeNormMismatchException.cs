using NGB.Tools.Exceptions;

namespace NGB.OperationalRegisters.Exceptions;

public sealed class OperationalRegisterCodeNormMismatchException(
    Guid registerId,
    string attemptedCode,
    string attemptedCodeNorm,
    string existingCode,
    string existingCodeNorm)
    : NgbConflictException(message: "Operational register code_norm mismatch for existing register id.",
        errorCode: Code,
        context: new Dictionary<string, object?>
        {
            ["registerId"] = registerId,
            ["attemptedCode"] = attemptedCode,
            ["attemptedCodeNorm"] = attemptedCodeNorm,
            ["existingCode"] = existingCode,
            ["existingCodeNorm"] = existingCodeNorm
        })
{
    public const string Code = "opreg.register.code_norm_mismatch";

    public Guid RegisterId { get; } = registerId;
}
