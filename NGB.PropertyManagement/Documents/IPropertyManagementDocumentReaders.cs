namespace NGB.PropertyManagement.Documents;

/// <summary>
/// PM typed readers used by posting/validation pipelines.
///
/// This lives in NGB.PropertyManagement (not Runtime) so that infrastructure modules
/// (e.g. NGB.PropertyManagement.PostgreSql) can implement it without depending on Runtime.
/// </summary>
public interface IPropertyManagementDocumentReaders
{
    Task<PmLeaseHead> ReadLeaseHeadAsync(Guid leaseId, CancellationToken ct = default);

    /// <summary>
    /// Reads pm.property head fields used by posting-time validators.
    /// </summary>
    Task<PmPropertyHead?> ReadPropertyHeadAsync(Guid propertyId, CancellationToken ct = default);

    /// <summary>
    /// Finds the first conflicting (overlapping) POSTED lease for the same property.
    ///
    /// PM invariant:
    /// - For the same property, POSTED leases must not overlap by date range.
    ///
    /// Notes:
    /// - Open-ended ranges (end == null) are treated as infinity.
    /// - This method is used by posting-time validators (not by the UI).
    /// </summary>
    Task<PmLeaseOverlapConflict?> FindFirstOverlappingPostedLeaseAsync(
        Guid currentLeaseId,
        Guid propertyId,
        DateOnly thisStartOnUtc,
        DateOnly? thisEndOnUtc,
        CancellationToken ct = default);

    Task<PmMaintenanceRequestHead> ReadMaintenanceRequestHeadAsync(Guid documentId, CancellationToken ct = default);
    Task<PmWorkOrderHead> ReadWorkOrderHeadAsync(Guid documentId, CancellationToken ct = default);
    Task<PmWorkOrderCompletionHead> ReadWorkOrderCompletionHeadAsync(Guid documentId, CancellationToken ct = default);
    Task<bool> ExistsOtherPostedWorkOrderCompletionAsync(
        Guid workOrderId,
        Guid? excludeDocumentId,
        CancellationToken ct = default);

    Task<PmRentChargeHead> ReadRentChargeHeadAsync(Guid documentId, CancellationToken ct = default);
    Task<PmReceivableChargeHead> ReadReceivableChargeHeadAsync(Guid documentId, CancellationToken ct = default);
    Task<PmLateFeeChargeHead> ReadLateFeeChargeHeadAsync(Guid documentId, CancellationToken ct = default);
    Task<PmReceivablePaymentHead> ReadReceivablePaymentHeadAsync(Guid documentId, CancellationToken ct = default);
    Task<PmReceivableReturnedPaymentHead> ReadReceivableReturnedPaymentHeadAsync(
        Guid documentId,
        CancellationToken ct = default);
    Task<PmReceivableCreditMemoHead> ReadReceivableCreditMemoHeadAsync(Guid documentId, CancellationToken ct = default);
    Task<PmReceivableApplyHead> ReadReceivableApplyHeadAsync(Guid documentId, CancellationToken ct = default);

    Task<PmPayableChargeHead> ReadPayableChargeHeadAsync(Guid documentId, CancellationToken ct = default);
    Task<PmPayablePaymentHead> ReadPayablePaymentHeadAsync(Guid documentId, CancellationToken ct = default);
    Task<PmPayableCreditMemoHead> ReadPayableCreditMemoHeadAsync(Guid documentId, CancellationToken ct = default);
    Task<PmPayableApplyHead> ReadPayableApplyHeadAsync(Guid documentId, CancellationToken ct = default);

    Task<IReadOnlyList<PmReceivableChargeHead>> ReadReceivableChargeHeadsAsync(
        IReadOnlyCollection<Guid> documentIds,
        CancellationToken ct = default);

    Task<IReadOnlyList<PmLateFeeChargeHead>> ReadLateFeeChargeHeadsAsync(
        IReadOnlyCollection<Guid> documentIds,
        CancellationToken ct = default);

    Task<IReadOnlyList<PmRentChargeHead>> ReadRentChargeHeadsAsync(
        IReadOnlyCollection<Guid> documentIds,
        CancellationToken ct = default);

    Task<IReadOnlyList<PmReceivablePaymentHead>> ReadReceivablePaymentHeadsAsync(
        IReadOnlyCollection<Guid> documentIds,
        CancellationToken ct = default);

    Task<IReadOnlyList<PmReceivableCreditMemoHead>> ReadReceivableCreditMemoHeadsAsync(
        IReadOnlyCollection<Guid> documentIds,
        CancellationToken ct = default);

    Task<IReadOnlyList<PmPayableChargeHead>> ReadPayableChargeHeadsAsync(
        IReadOnlyCollection<Guid> documentIds,
        CancellationToken ct = default);

    Task<IReadOnlyList<PmPayablePaymentHead>> ReadPayablePaymentHeadsAsync(
        IReadOnlyCollection<Guid> documentIds,
        CancellationToken ct = default);

    Task<IReadOnlyList<PmPayableCreditMemoHead>> ReadPayableCreditMemoHeadsAsync(
        IReadOnlyCollection<Guid> documentIds,
        CancellationToken ct = default);

    /// <summary>
    /// Reads active (posted) receivable apply allocations for the given lease context.
    ///
    /// This is the canonical explainability read-model behind the receivables screen.
    /// It intentionally surfaces allocations as first-class rows instead of requiring
    /// callers to reconstruct them from graph edges or raw open-items movements.
    /// </summary>
    Task<IReadOnlyList<PmReceivableAllocationRead>> ReadActiveReceivableAllocationsAsync(
        Guid partyId,
        Guid propertyId,
        Guid leaseId,
        CancellationToken ct = default);

    Task<IReadOnlyList<PmPayableAllocationRead>> ReadActivePayableAllocationsAsync(
        Guid partyId,
        Guid propertyId,
        DateOnly? fromMonthInclusive = null,
        DateOnly? toMonthInclusive = null,
        CancellationToken ct = default);

    Task<DateOnly?> ReadFirstPayablesActivityMonthAsync(
        Guid partyId,
        Guid propertyId,
        CancellationToken ct = default);

    Task<IReadOnlyList<PmChargeTypeHead>> ReadChargeTypeHeadsAsync(
        IReadOnlyCollection<Guid> chargeTypeIds,
        CancellationToken ct = default);

    Task<PmChargeTypeHead> ReadChargeTypeHeadAsync(Guid chargeTypeId, CancellationToken ct = default);

    Task<IReadOnlyList<PmPayableChargeTypeHead>> ReadPayableChargeTypeHeadsAsync(
        IReadOnlyCollection<Guid> chargeTypeIds,
        CancellationToken ct = default);

    Task<PmPayableChargeTypeHead> ReadPayableChargeTypeHeadAsync(Guid chargeTypeId, CancellationToken ct = default);

    /// <summary>
    /// Reads minimal document header info (type + number) in bulk.
    /// Used by UI/report read-models to avoid N+1 when enriching PM typed docs.
    /// </summary>
    Task<IReadOnlyList<PmDocumentInfo>> ReadDocumentInfosAsync(
        IReadOnlyCollection<Guid> documentIds,
        CancellationToken ct = default);
}

public sealed record PmLeaseHead(
    Guid LeaseId,
    Guid PrimaryPartyId,
    Guid PropertyId,
    DateOnly StartOnUtc,
    DateOnly? EndOnUtc);

public sealed record PmLeaseOverlapConflict(Guid LeaseId, DateOnly StartOnUtc, DateOnly? EndOnUtc);

public sealed record PmMaintenanceRequestHead(
    Guid DocumentId,
    Guid PropertyId,
    Guid PartyId,
    Guid CategoryId,
    string Priority,
    string Subject,
    string? Description,
    DateOnly RequestedAtUtc);

public sealed record PmWorkOrderHead(
    Guid DocumentId,
    Guid RequestId,
    Guid? AssignedPartyId,
    string? ScopeOfWork,
    DateOnly? DueByUtc,
    string CostResponsibility);

public sealed record PmWorkOrderCompletionHead(
    Guid DocumentId,
    Guid WorkOrderId,
    DateOnly ClosedAtUtc,
    string Outcome,
    string? ResolutionNotes);

public sealed record PmRentChargeHead(
    Guid DocumentId,
    Guid LeaseId,
    Guid PartyId,
    Guid PropertyId,
    DateOnly PeriodFromUtc,
    DateOnly PeriodToUtc,
    DateOnly DueOnUtc,
    decimal Amount,
    string? Memo);

public sealed record PmReceivableChargeHead(
    Guid DocumentId,
    Guid PartyId,
    Guid PropertyId,
    Guid LeaseId,
    Guid ChargeTypeId,
    DateOnly DueOnUtc,
    decimal Amount,
    string? Memo);

public sealed record PmLateFeeChargeHead(
    Guid DocumentId,
    Guid PartyId,
    Guid PropertyId,
    Guid LeaseId,
    DateOnly DueOnUtc,
    decimal Amount,
    string? Memo);

public sealed record PmReceivablePaymentHead(
    Guid DocumentId,
    Guid PartyId,
    Guid PropertyId,
    Guid LeaseId,
    Guid? BankAccountId,
    DateOnly ReceivedOnUtc,
    decimal Amount,
    string? Memo);

public sealed record PmReceivableReturnedPaymentHead(
    Guid DocumentId,
    Guid PartyId,
    Guid PropertyId,
    Guid LeaseId,
    Guid OriginalPaymentId,
    Guid? BankAccountId,
    DateOnly ReturnedOnUtc,
    decimal Amount,
    string? Memo);

public sealed record PmReceivableCreditMemoHead(
    Guid DocumentId,
    Guid PartyId,
    Guid PropertyId,
    Guid LeaseId,
    Guid? ChargeTypeId,
    DateOnly CreditedOnUtc,
    decimal Amount,
    string? Memo);

public sealed record PmPayableChargeHead(
    Guid DocumentId,
    Guid PartyId,
    Guid PropertyId,
    Guid ChargeTypeId,
    DateOnly DueOnUtc,
    decimal Amount,
    string? VendorInvoiceNo,
    string? Memo);

public sealed record PmPayablePaymentHead(
    Guid DocumentId,
    Guid PartyId,
    Guid PropertyId,
    Guid? BankAccountId,
    DateOnly PaidOnUtc,
    decimal Amount,
    string? Memo);

public sealed record PmPayableCreditMemoHead(
    Guid DocumentId,
    Guid PartyId,
    Guid PropertyId,
    Guid ChargeTypeId,
    DateOnly CreditedOnUtc,
    decimal Amount,
    string? Memo);

public sealed record PmPayableApplyHead(
    Guid DocumentId,
    Guid CreditDocumentId,
    Guid ChargeDocumentId,
    DateOnly AppliedOnUtc,
    decimal Amount,
    string? Memo);

public sealed record PmReceivableApplyHead(
    Guid DocumentId,
    Guid CreditDocumentId,
    Guid ChargeDocumentId,
    DateOnly AppliedOnUtc,
    decimal Amount,
    string? Memo);

public sealed record PmPayableAllocationRead(
    Guid ApplyId,
    string? ApplyDisplay,
    string? ApplyNumber,
    Guid CreditDocumentId,
    string CreditDocumentType,
    string? CreditDocumentDisplay,
    string? CreditDocumentNumber,
    Guid ChargeDocumentId,
    string ChargeDocumentType,
    string? ChargeDisplay,
    string? ChargeNumber,
    DateOnly AppliedOnUtc,
    decimal Amount,
    bool IsPosted);

public sealed record PmReceivableAllocationRead(
    Guid ApplyId,
    string? ApplyDisplay,
    string? ApplyNumber,
    Guid CreditDocumentId,
    string CreditDocumentType,
    string? CreditDocumentDisplay,
    string? CreditDocumentNumber,
    Guid ChargeDocumentId,
    string ChargeDocumentType,
    string? ChargeDisplay,
    string? ChargeNumber,
    DateOnly AppliedOnUtc,
    decimal Amount,
    bool IsPosted);

public sealed record PmChargeTypeHead(Guid ChargeTypeId, string Display, Guid? CreditAccountId);

public sealed record PmPayableChargeTypeHead(Guid ChargeTypeId, string Display, Guid? DebitAccountId);

public sealed record PmDocumentInfo(Guid DocumentId, string TypeCode, string? Number);

public sealed record PmPropertyHead(Guid PropertyId, string Kind, Guid? ParentPropertyId, bool IsDeleted);
