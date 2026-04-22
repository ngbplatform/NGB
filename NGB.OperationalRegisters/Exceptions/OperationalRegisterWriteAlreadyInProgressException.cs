using NGB.Tools.Exceptions;

namespace NGB.OperationalRegisters.Exceptions;

public sealed class OperationalRegisterWriteAlreadyInProgressException(
    Guid registerId,
    Guid documentId,
    string operation)
    : NgbConflictException(
        message: "Operational register write is already in progress.",
        errorCode: Code,
        context: new Dictionary<string, object?>
        {
            ["registerId"] = registerId,
            ["documentId"] = documentId,
            ["operation"] = operation
        })
{
    public const string Code = "opreg.write.in_progress";

    public Guid RegisterId { get; } = registerId;
    public Guid DocumentId { get; } = documentId;
    public string Operation { get; } = operation;
}
