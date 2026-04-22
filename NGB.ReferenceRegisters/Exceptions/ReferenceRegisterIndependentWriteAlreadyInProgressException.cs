using NGB.Tools.Exceptions;

namespace NGB.ReferenceRegisters.Exceptions;

public sealed class ReferenceRegisterIndependentWriteAlreadyInProgressException(
    Guid registerId,
    Guid commandId,
    string operation)
    : NgbConflictException(message: "Reference register independent write is already in progress.",
        errorCode: Code,
        context: new Dictionary<string, object?>
        {
            ["registerId"] = registerId,
            ["commandId"] = commandId,
            ["operation"] = operation
        })
{
    public const string Code = "refreg.independent_write.in_progress";

    public Guid RegisterId { get; } = registerId;
    public Guid CommandId { get; } = commandId;
    public string Operation { get; } = operation;
}
