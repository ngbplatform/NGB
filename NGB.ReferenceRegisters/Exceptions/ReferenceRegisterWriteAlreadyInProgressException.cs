using NGB.Tools.Exceptions;

namespace NGB.ReferenceRegisters.Exceptions;

public sealed class ReferenceRegisterWriteAlreadyInProgressException(
    Guid registerId,
    Guid documentId,
    string operation)
    : NgbConflictException(
        message: "Reference register write is already in progress.",
        errorCode: Code,
        context: new Dictionary<string, object?>
        {
            ["registerId"] = registerId,
            ["documentId"] = documentId,
            ["operation"] = operation
        })
{
    public const string Code = "refreg.write.in_progress";

    public Guid RegisterId { get; } = registerId;
    public Guid DocumentId { get; } = documentId;
    public string Operation { get; } = operation;
}
