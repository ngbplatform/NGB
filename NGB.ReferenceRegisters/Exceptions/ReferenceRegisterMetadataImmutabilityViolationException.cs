using NGB.Tools.Exceptions;

namespace NGB.ReferenceRegisters.Exceptions;

public sealed class ReferenceRegisterMetadataImmutabilityViolationException(
    Guid registerId,
    string reason,
    object? details = null)
    : NgbConflictException(
        message: "Reference register metadata is immutable after records exist.",
        errorCode: Code,
        context: new Dictionary<string, object?>
        {
            ["registerId"] = registerId,
            ["reason"] = reason,
            ["details"] = details,
        })
{
    public const string Code = "refreg.metadata.immutability_violation";

    public Guid RegisterId { get; } = registerId;
    public string Reason { get; } = reason;
}
