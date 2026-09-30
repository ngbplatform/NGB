using NGB.Persistence.Documents;
using NGB.PropertyManagement.Documents;

namespace NGB.PropertyManagement.PostgreSql.Documents;

internal sealed class PostingCachedPropertyManagementDocumentReaders(
    IPropertyManagementDocumentReaders inner,
    IDocumentPostingReadCache cache)
    : IPropertyManagementDocumentReaders
{
    public Task<PmLeaseHead> ReadLeaseHeadAsync(Guid leaseId, CancellationToken ct = default)
        => ReadAsync(leaseId, nameof(ReadLeaseHeadAsync), inner.ReadLeaseHeadAsync, ct);

    public Task<PmPropertyHead?> ReadPropertyHeadAsync(Guid propertyId, CancellationToken ct = default)
        => ReadAsync(propertyId, nameof(ReadPropertyHeadAsync), inner.ReadPropertyHeadAsync, ct);

    public Task<PmLeaseOverlapConflict?> FindFirstOverlappingPostedLeaseAsync(
        Guid currentLeaseId,
        Guid propertyId,
        DateOnly thisStartOnUtc,
        DateOnly? thisEndOnUtc,
        CancellationToken ct = default)
        => inner.FindFirstOverlappingPostedLeaseAsync(currentLeaseId, propertyId, thisStartOnUtc, thisEndOnUtc, ct);

    public Task<PmMaintenanceRequestHead> ReadMaintenanceRequestHeadAsync(Guid documentId, CancellationToken ct = default)
        => ReadAsync(documentId, nameof(ReadMaintenanceRequestHeadAsync), inner.ReadMaintenanceRequestHeadAsync, ct);

    public Task<PmWorkOrderHead> ReadWorkOrderHeadAsync(Guid documentId, CancellationToken ct = default)
        => ReadAsync(documentId, nameof(ReadWorkOrderHeadAsync), inner.ReadWorkOrderHeadAsync, ct);

    public Task<PmWorkOrderCompletionHead> ReadWorkOrderCompletionHeadAsync(Guid documentId, CancellationToken ct = default)
        => ReadAsync(documentId, nameof(ReadWorkOrderCompletionHeadAsync), inner.ReadWorkOrderCompletionHeadAsync, ct);

    public Task<bool> ExistsOtherPostedWorkOrderCompletionAsync(
        Guid workOrderId,
        Guid? excludeDocumentId,
        CancellationToken ct = default)
        => inner.ExistsOtherPostedWorkOrderCompletionAsync(workOrderId, excludeDocumentId, ct);

    public Task<PmRentChargeHead> ReadRentChargeHeadAsync(Guid documentId, CancellationToken ct = default)
        => ReadAsync(documentId, nameof(ReadRentChargeHeadAsync), inner.ReadRentChargeHeadAsync, ct);

    public Task<PmReceivableChargeHead> ReadReceivableChargeHeadAsync(Guid documentId, CancellationToken ct = default)
        => ReadAsync(documentId, nameof(ReadReceivableChargeHeadAsync), inner.ReadReceivableChargeHeadAsync, ct);

    public Task<PmLateFeeChargeHead> ReadLateFeeChargeHeadAsync(Guid documentId, CancellationToken ct = default)
        => ReadAsync(documentId, nameof(ReadLateFeeChargeHeadAsync), inner.ReadLateFeeChargeHeadAsync, ct);

    public Task<PmReceivablePaymentHead> ReadReceivablePaymentHeadAsync(Guid documentId, CancellationToken ct = default)
        => ReadAsync(documentId, nameof(ReadReceivablePaymentHeadAsync), inner.ReadReceivablePaymentHeadAsync, ct);

    public Task<PmReceivableReturnedPaymentHead> ReadReceivableReturnedPaymentHeadAsync(Guid documentId, CancellationToken ct = default)
        => ReadAsync(documentId, nameof(ReadReceivableReturnedPaymentHeadAsync), inner.ReadReceivableReturnedPaymentHeadAsync, ct);

    public Task<PmReceivableCreditMemoHead> ReadReceivableCreditMemoHeadAsync(Guid documentId, CancellationToken ct = default)
        => ReadAsync(documentId, nameof(ReadReceivableCreditMemoHeadAsync), inner.ReadReceivableCreditMemoHeadAsync, ct);

    public Task<PmReceivableApplyHead> ReadReceivableApplyHeadAsync(Guid documentId, CancellationToken ct = default)
        => ReadAsync(documentId, nameof(ReadReceivableApplyHeadAsync), inner.ReadReceivableApplyHeadAsync, ct);

    public Task<PmPayableChargeHead> ReadPayableChargeHeadAsync(Guid documentId, CancellationToken ct = default)
        => ReadAsync(documentId, nameof(ReadPayableChargeHeadAsync), inner.ReadPayableChargeHeadAsync, ct);

    public Task<PmPayablePaymentHead> ReadPayablePaymentHeadAsync(Guid documentId, CancellationToken ct = default)
        => ReadAsync(documentId, nameof(ReadPayablePaymentHeadAsync), inner.ReadPayablePaymentHeadAsync, ct);

    public Task<PmPayableCreditMemoHead> ReadPayableCreditMemoHeadAsync(Guid documentId, CancellationToken ct = default)
        => ReadAsync(documentId, nameof(ReadPayableCreditMemoHeadAsync), inner.ReadPayableCreditMemoHeadAsync, ct);

    public Task<PmPayableApplyHead> ReadPayableApplyHeadAsync(Guid documentId, CancellationToken ct = default)
        => ReadAsync(documentId, nameof(ReadPayableApplyHeadAsync), inner.ReadPayableApplyHeadAsync, ct);

    public Task<IReadOnlyList<PmReceivableChargeHead>> ReadReceivableChargeHeadsAsync(IReadOnlyCollection<Guid> documentIds, CancellationToken ct = default)
        => inner.ReadReceivableChargeHeadsAsync(documentIds, ct);

    public Task<IReadOnlyList<PmLateFeeChargeHead>> ReadLateFeeChargeHeadsAsync(IReadOnlyCollection<Guid> documentIds, CancellationToken ct = default)
        => inner.ReadLateFeeChargeHeadsAsync(documentIds, ct);

    public Task<IReadOnlyList<PmRentChargeHead>> ReadRentChargeHeadsAsync(IReadOnlyCollection<Guid> documentIds, CancellationToken ct = default)
        => inner.ReadRentChargeHeadsAsync(documentIds, ct);

    public Task<IReadOnlyList<PmReceivablePaymentHead>> ReadReceivablePaymentHeadsAsync(IReadOnlyCollection<Guid> documentIds, CancellationToken ct = default)
        => inner.ReadReceivablePaymentHeadsAsync(documentIds, ct);

    public Task<IReadOnlyList<PmReceivableCreditMemoHead>> ReadReceivableCreditMemoHeadsAsync(IReadOnlyCollection<Guid> documentIds, CancellationToken ct = default)
        => inner.ReadReceivableCreditMemoHeadsAsync(documentIds, ct);

    public Task<IReadOnlyList<PmPayableChargeHead>> ReadPayableChargeHeadsAsync(IReadOnlyCollection<Guid> documentIds, CancellationToken ct = default)
        => inner.ReadPayableChargeHeadsAsync(documentIds, ct);

    public Task<IReadOnlyList<PmPayablePaymentHead>> ReadPayablePaymentHeadsAsync(IReadOnlyCollection<Guid> documentIds, CancellationToken ct = default)
        => inner.ReadPayablePaymentHeadsAsync(documentIds, ct);

    public Task<IReadOnlyList<PmPayableCreditMemoHead>> ReadPayableCreditMemoHeadsAsync(IReadOnlyCollection<Guid> documentIds, CancellationToken ct = default)
        => inner.ReadPayableCreditMemoHeadsAsync(documentIds, ct);

    public Task<IReadOnlyList<PmReceivableAllocationRead>> ReadActiveReceivableAllocationsAsync(
        Guid partyId,
        Guid propertyId,
        Guid leaseId,
        CancellationToken ct = default)
        => inner.ReadActiveReceivableAllocationsAsync(partyId, propertyId, leaseId, ct);

    public Task<IReadOnlyList<PmPayableAllocationRead>> ReadActivePayableAllocationsAsync(
        Guid partyId,
        Guid propertyId,
        DateOnly? fromMonthInclusive = null,
        DateOnly? toMonthInclusive = null,
        CancellationToken ct = default)
        => inner.ReadActivePayableAllocationsAsync(partyId, propertyId, fromMonthInclusive, toMonthInclusive, ct);

    public Task<DateOnly?> ReadFirstPayablesActivityMonthAsync(Guid partyId, Guid propertyId, CancellationToken ct = default)
        => inner.ReadFirstPayablesActivityMonthAsync(partyId, propertyId, ct);

    public Task<IReadOnlyList<PmChargeTypeHead>> ReadChargeTypeHeadsAsync(IReadOnlyCollection<Guid> chargeTypeIds, CancellationToken ct = default)
        => inner.ReadChargeTypeHeadsAsync(chargeTypeIds, ct);

    public Task<PmChargeTypeHead> ReadChargeTypeHeadAsync(Guid chargeTypeId, CancellationToken ct = default)
        => ReadAsync(chargeTypeId, nameof(ReadChargeTypeHeadAsync), inner.ReadChargeTypeHeadAsync, ct);

    public Task<IReadOnlyList<PmPayableChargeTypeHead>> ReadPayableChargeTypeHeadsAsync(IReadOnlyCollection<Guid> chargeTypeIds, CancellationToken ct = default)
        => inner.ReadPayableChargeTypeHeadsAsync(chargeTypeIds, ct);

    public Task<PmPayableChargeTypeHead> ReadPayableChargeTypeHeadAsync(Guid chargeTypeId, CancellationToken ct = default)
        => ReadAsync(chargeTypeId, nameof(ReadPayableChargeTypeHeadAsync), inner.ReadPayableChargeTypeHeadAsync, ct);

    public Task<IReadOnlyList<PmDocumentInfo>> ReadDocumentInfosAsync(IReadOnlyCollection<Guid> documentIds, CancellationToken ct = default)
        => inner.ReadDocumentInfosAsync(documentIds, ct);

    private Task<T> ReadAsync<T>(
        Guid id,
        string operation,
        Func<Guid, CancellationToken, Task<T>> reader,
        CancellationToken ct)
        => cache.GetOrAddAsync(
            CacheKey(id, operation),
            innerCt => reader(id, innerCt),
            ct);

    internal static string CacheKey(Guid id, string operation) => $"property-management:{operation}:{id:D}";
}
