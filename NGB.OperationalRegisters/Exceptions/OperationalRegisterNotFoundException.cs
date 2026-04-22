using NGB.Tools.Exceptions;

namespace NGB.OperationalRegisters.Exceptions;

public sealed class OperationalRegisterNotFoundException(Guid registerId) : NgbNotFoundException(
    message: $"Operational register '{registerId}' was not found.",
    errorCode: Code,
    context: new Dictionary<string, object?>
    {
        ["registerId"] = registerId
    })
{
    public const string Code = "opreg.register.not_found";

    public Guid RegisterId { get; } = registerId;
}
