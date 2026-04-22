using NGB.Tools.Exceptions;

namespace NGB.ReferenceRegisters.Exceptions;

public sealed class ReferenceRegisterCodeNormMismatchException(
    Guid registerId,
    string attemptedCode,
    string attemptedCodeNorm,
    string existingCode,
    string existingCodeNorm)
    : NgbConflictException(
        message: "Reference register code_norm mismatch for existing register id.",
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
    public const string Code = "refreg.register.code_norm_mismatch";

    public Guid RegisterId { get; } = registerId;
}
