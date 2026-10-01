using NGB.Attachments;
using NGB.Contracts.BusinessObjects;

namespace NGB.Persistence.Attachments;

public interface IAttachmentRepository
{
    Task LockTargetAsync(BusinessObjectRef target, CancellationToken ct);
    Task<long> CountReservedAsync(BusinessObjectRef target, CancellationToken ct);
    Task<AttachmentRecord?> GetAsync(Guid id, bool forUpdate, CancellationToken ct);

    Task<IReadOnlyList<AttachmentRecord>> ListAsync(
        BusinessObjectRef target,
        int take,
        Guid? cursor,
        CancellationToken ct);

    Task InsertAsync(AttachmentRecord record, CancellationToken ct);
    Task SaveAsync(AttachmentRecord record, CancellationToken ct);
    Task<IReadOnlyList<AttachmentRecord>> LockStalePendingAsync(DateTime beforeUtc, int take, CancellationToken ct);
}

public interface IBusinessObjectContentSummaryReader
{
    Task<BusinessObjectContentSummary> GetAsync(
        BusinessObjectRef target,
        bool attachments,
        bool notes,
        CancellationToken ct);
}
