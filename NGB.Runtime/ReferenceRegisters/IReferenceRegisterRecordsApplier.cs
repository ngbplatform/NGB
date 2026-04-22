using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;

namespace NGB.Runtime.ReferenceRegisters;

public interface IReferenceRegisterRecordsApplier
{
    Task<ReferenceRegisterWriteResult> ApplyRecordsForDocumentAsync(
        Guid registerId,
        Guid documentId,
        ReferenceRegisterWriteOperation operation,
        IReadOnlyList<ReferenceRegisterRecordWrite> recordsToApply,
        bool manageTransaction = true,
        CancellationToken ct = default);
}
