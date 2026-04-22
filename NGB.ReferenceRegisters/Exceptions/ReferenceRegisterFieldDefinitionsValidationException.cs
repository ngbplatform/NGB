using NGB.Tools.Exceptions;

namespace NGB.ReferenceRegisters.Exceptions;

public sealed class ReferenceRegisterFieldDefinitionsValidationException(
    Guid registerId,
    string reason,
    object? details = null)
    : NgbValidationException(message: "Reference register field definitions validation failed.",
        errorCode: Code,
        context: new Dictionary<string, object?>
        {
            ["registerId"] = registerId,
            ["reason"] = reason,
            ["details"] = details,
        })
{
    public const string Code = "refreg.fields.validation_failed";

    public Guid RegisterId { get; } = registerId;
    public string Reason { get; } = reason;
}
