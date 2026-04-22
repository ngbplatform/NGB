using NGB.Tools.Exceptions;

namespace NGB.ReferenceRegisters.Exceptions;

public sealed class ReferenceRegisterNotFoundException(Guid registerId) : NgbNotFoundException(
    message: "Reference register was not found.",
    errorCode: Code,
    context: new Dictionary<string, object?>
    {
        ["registerId"] = registerId
    })
{
    public const string Code = "refreg.register.not_found";

    public Guid RegisterId { get; } = registerId;
}
