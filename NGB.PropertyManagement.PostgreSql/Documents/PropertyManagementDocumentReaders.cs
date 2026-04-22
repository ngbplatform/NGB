using Dapper;
using NGB.Core.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Documents;

namespace NGB.PropertyManagement.PostgreSql.Documents;

public sealed class PropertyManagementDocumentReaders(IUnitOfWork uow) : IPropertyManagementDocumentReaders
{
    public async Task<PmLeaseHead> ReadLeaseHeadAsync(Guid leaseId, CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
SELECT
    l.document_id AS LeaseId,
    p.party_id AS PrimaryPartyId,
    l.property_id AS PropertyId,
    l.start_on_utc AS StartOnUtc,
    l.end_on_utc AS EndOnUtc
FROM doc_pm_lease l
JOIN doc_pm_lease__parties p
  ON p.document_id = l.document_id
 AND p.is_primary = TRUE
WHERE l.document_id = @lease_id;
""";

        return await uow.Connection.QuerySingleAsync<PmLeaseHead>(
            new CommandDefinition(sql, new
                {
                    lease_id = leaseId
                }, 
                uow.Transaction,
                cancellationToken: ct));
    }

    public async Task<PmPropertyHead?> ReadPropertyHeadAsync(Guid propertyId, CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
SELECT
    p.catalog_id AS PropertyId,
    p.kind AS Kind,
    p.parent_property_id AS ParentPropertyId,
    c.is_deleted AS IsDeleted
FROM cat_pm_property p
JOIN catalogs c ON c.id = p.catalog_id
WHERE p.catalog_id = @property_id;
""";

        return await uow.Connection.QuerySingleOrDefaultAsync<PmPropertyHead>(
            new CommandDefinition(sql, new
                {
                    property_id = propertyId
                }, 
                uow.Transaction,
                cancellationToken: ct));
    }

    public async Task<PmLeaseOverlapConflict?> FindFirstOverlappingPostedLeaseAsync(
        Guid currentLeaseId,
        Guid propertyId,
        DateOnly thisStartOnUtc,
        DateOnly? thisEndOnUtc,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        // Overlap check (open-ended ranges are treated as infinity):
        // other.start <= this.end AND this.start <= other.end
        const string sql = """
SELECT
    d.id AS LeaseId,
    l.start_on_utc AS StartOnUtc,
    l.end_on_utc AS EndOnUtc
FROM documents d
JOIN doc_pm_lease l ON l.document_id = d.id
WHERE d.status = @posted
  AND d.id <> @current_lease_id
  AND l.property_id = @property_id
  AND l.start_on_utc <= COALESCE(@this_end, 'infinity'::date)
  AND @this_start <= COALESCE(l.end_on_utc, 'infinity'::date)
ORDER BY l.start_on_utc
LIMIT 1;
""";

        return await uow.Connection.QuerySingleOrDefaultAsync<PmLeaseOverlapConflict>(
            new CommandDefinition(
                sql,
                new
                {
                    posted = (int)DocumentStatus.Posted,
                    current_lease_id = currentLeaseId,
                    property_id = propertyId,
                    this_start = thisStartOnUtc,
                    this_end = thisEndOnUtc
                },
                uow.Transaction,
                cancellationToken: ct));
    }

    public async Task<PmMaintenanceRequestHead> ReadMaintenanceRequestHeadAsync(
        Guid documentId,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
SELECT
    document_id AS DocumentId,
    property_id AS PropertyId,
    party_id AS PartyId,
    category_id AS CategoryId,
    priority AS Priority,
    subject AS Subject,
    description AS Description,
    requested_at_utc AS RequestedAtUtc
FROM doc_pm_maintenance_request
WHERE document_id = @document_id;
""";

        return await uow.Connection.QuerySingleAsync<PmMaintenanceRequestHead>(
            new CommandDefinition(sql, new
                {
                    document_id = documentId
                }, 
                uow.Transaction,
                cancellationToken: ct));
    }

    public async Task<PmWorkOrderHead> ReadWorkOrderHeadAsync(Guid documentId, CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
SELECT
    document_id AS DocumentId,
    request_id AS RequestId,
    assigned_party_id AS AssignedPartyId,
    scope_of_work AS ScopeOfWork,
    due_by_utc AS DueByUtc,
    cost_responsibility AS CostResponsibility
FROM doc_pm_work_order
WHERE document_id = @document_id;
""";

        return await uow.Connection.QuerySingleAsync<PmWorkOrderHead>(
            new CommandDefinition(sql, new
                {
                    document_id = documentId
                }, 
                uow.Transaction,
                cancellationToken: ct));
    }

    public async Task<PmWorkOrderCompletionHead> ReadWorkOrderCompletionHeadAsync(
        Guid documentId,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
SELECT
    document_id AS DocumentId,
    work_order_id AS WorkOrderId,
    closed_at_utc AS ClosedAtUtc,
    outcome AS Outcome,
    resolution_notes AS ResolutionNotes
FROM doc_pm_work_order_completion
WHERE document_id = @document_id;
""";

        return await uow.Connection.QuerySingleAsync<PmWorkOrderCompletionHead>(
            new CommandDefinition(sql, new
                {
                    document_id = documentId
                },
                uow.Transaction,
                cancellationToken: ct));
    }

    public async Task<bool> ExistsOtherPostedWorkOrderCompletionAsync(
        Guid workOrderId,
        Guid? excludeDocumentId,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
SELECT EXISTS (
    SELECT 1
      FROM doc_pm_work_order_completion c
      JOIN documents d
        ON d.id = c.document_id
     WHERE c.work_order_id = @work_order_id
       AND d.status = 2
       AND (@exclude_document_id IS NULL OR c.document_id <> @exclude_document_id)
);
""";

        return await uow.Connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(sql, new
                {
                    work_order_id = workOrderId,
                    exclude_document_id = excludeDocumentId
                }, 
                uow.Transaction,
                cancellationToken: ct));
    }

    public async Task<PmRentChargeHead> ReadRentChargeHeadAsync(Guid documentId, CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
SELECT
    rc.document_id AS DocumentId,
    rc.lease_id AS LeaseId,
    p.party_id AS PartyId,
    l.property_id AS PropertyId,
    rc.period_from_utc AS PeriodFromUtc,
    rc.period_to_utc AS PeriodToUtc,
    rc.due_on_utc AS DueOnUtc,
    rc.amount AS Amount,
    rc.memo AS Memo
FROM doc_pm_rent_charge rc
JOIN doc_pm_lease l
  ON l.document_id = rc.lease_id
JOIN doc_pm_lease__parties p
  ON p.document_id = l.document_id
 AND p.is_primary = TRUE
WHERE rc.document_id = @document_id;
""";

        return await uow.Connection.QuerySingleAsync<PmRentChargeHead>(
            new CommandDefinition(sql, new
                {
                    document_id = documentId
                }, 
                uow.Transaction,
                cancellationToken: ct));
    }

    public async Task<PmReceivableChargeHead> ReadReceivableChargeHeadAsync(
        Guid documentId,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
SELECT
    rc.document_id AS DocumentId,
    p.party_id AS PartyId,
    l.property_id AS PropertyId,
    rc.lease_id AS LeaseId,
    rc.charge_type_id AS ChargeTypeId,
    rc.due_on_utc AS DueOnUtc,
    rc.amount AS Amount,
    rc.memo AS Memo
FROM doc_pm_receivable_charge rc
JOIN doc_pm_lease l
  ON l.document_id = rc.lease_id
JOIN doc_pm_lease__parties p
  ON p.document_id = l.document_id
 AND p.is_primary = TRUE
WHERE rc.document_id = @document_id;
""";

        return await uow.Connection.QuerySingleAsync<PmReceivableChargeHead>(
            new CommandDefinition(sql, new
                {
                    document_id = documentId
                }, 
                uow.Transaction,
                cancellationToken: ct));
    }

    public async Task<PmLateFeeChargeHead> ReadLateFeeChargeHeadAsync(
        Guid documentId,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
SELECT
    lfc.document_id AS DocumentId,
    p.party_id AS PartyId,
    l.property_id AS PropertyId,
    lfc.lease_id AS LeaseId,
    lfc.due_on_utc AS DueOnUtc,
    lfc.amount AS Amount,
    lfc.memo AS Memo
FROM doc_pm_late_fee_charge lfc
JOIN doc_pm_lease l
  ON l.document_id = lfc.lease_id
JOIN doc_pm_lease__parties p
  ON p.document_id = l.document_id
 AND p.is_primary = TRUE
WHERE lfc.document_id = @document_id;
""";

        return await uow.Connection.QuerySingleAsync<PmLateFeeChargeHead>(
            new CommandDefinition(sql, new
                {
                    document_id = documentId
                }, 
                uow.Transaction,
                cancellationToken: ct));
    }

    public async Task<PmReceivablePaymentHead> ReadReceivablePaymentHeadAsync(
        Guid documentId,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
SELECT
    rp.document_id AS DocumentId,
    p.party_id AS PartyId,
    l.property_id AS PropertyId,
    rp.lease_id AS LeaseId,
    rp.bank_account_id AS BankAccountId,
    rp.received_on_utc AS ReceivedOnUtc,
    rp.amount AS Amount,
    rp.memo AS Memo
FROM doc_pm_receivable_payment rp
JOIN doc_pm_lease l
  ON l.document_id = rp.lease_id
JOIN doc_pm_lease__parties p
  ON p.document_id = l.document_id
 AND p.is_primary = TRUE
WHERE rp.document_id = @document_id;
""";

        return await uow.Connection.QuerySingleAsync<PmReceivablePaymentHead>(
            new CommandDefinition(sql, new
                {
                    document_id = documentId
                }, 
                uow.Transaction, 
                cancellationToken: ct));
    }

    public async Task<PmReceivableReturnedPaymentHead> ReadReceivableReturnedPaymentHeadAsync(
        Guid documentId,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
SELECT
    rrp.document_id AS DocumentId,
    p.party_id AS PartyId,
    l.property_id AS PropertyId,
    rp.lease_id AS LeaseId,
    rrp.original_payment_id AS OriginalPaymentId,
    rp.bank_account_id AS BankAccountId,
    rrp.returned_on_utc AS ReturnedOnUtc,
    rrp.amount AS Amount,
    rrp.memo AS Memo
FROM doc_pm_receivable_returned_payment rrp
JOIN doc_pm_receivable_payment rp
  ON rp.document_id = rrp.original_payment_id
JOIN doc_pm_lease l
  ON l.document_id = rp.lease_id
JOIN doc_pm_lease__parties p
  ON p.document_id = l.document_id
 AND p.is_primary = TRUE
WHERE rrp.document_id = @document_id;
""";

        return await uow.Connection.QuerySingleAsync<PmReceivableReturnedPaymentHead>(
            new CommandDefinition(sql, new
                {
                    document_id = documentId
                }, 
                uow.Transaction,
                cancellationToken: ct));
    }

    public async Task<PmReceivableCreditMemoHead> ReadReceivableCreditMemoHeadAsync(
        Guid documentId,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
SELECT
    rcm.document_id AS DocumentId,
    p.party_id AS PartyId,
    l.property_id AS PropertyId,
    rcm.lease_id AS LeaseId,
    rcm.charge_type_id AS ChargeTypeId,
    rcm.credited_on_utc AS CreditedOnUtc,
    rcm.amount AS Amount,
    rcm.memo AS Memo
FROM doc_pm_receivable_credit_memo rcm
JOIN doc_pm_lease l
  ON l.document_id = rcm.lease_id
JOIN doc_pm_lease__parties p
  ON p.document_id = l.document_id
 AND p.is_primary = TRUE
WHERE rcm.document_id = @document_id;
""";

        return await uow.Connection.QuerySingleAsync<PmReceivableCreditMemoHead>(
            new CommandDefinition(sql, new
                {
                    document_id = documentId
                }, 
                uow.Transaction,
                cancellationToken: ct));
    }

    public async Task<PmPayableChargeHead> ReadPayableChargeHeadAsync(
        Guid documentId,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
SELECT
    document_id AS DocumentId,
    party_id AS PartyId,
    property_id AS PropertyId,
    charge_type_id AS ChargeTypeId,
    due_on_utc AS DueOnUtc,
    amount AS Amount,
    vendor_invoice_no AS VendorInvoiceNo,
    memo AS Memo
FROM doc_pm_payable_charge
WHERE document_id = @document_id;
""";

        return await uow.Connection.QuerySingleAsync<PmPayableChargeHead>(
            new CommandDefinition(sql, new
                {
                    document_id = documentId
                }, 
                uow.Transaction,
                cancellationToken: ct));
    }

    public async Task<PmPayablePaymentHead> ReadPayablePaymentHeadAsync(
        Guid documentId,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
SELECT
    document_id AS DocumentId,
    party_id AS PartyId,
    property_id AS PropertyId,
    bank_account_id AS BankAccountId,
    paid_on_utc AS PaidOnUtc,
    amount AS Amount,
    memo AS Memo
FROM doc_pm_payable_payment
WHERE document_id = @document_id;
""";

        return await uow.Connection.QuerySingleAsync<PmPayablePaymentHead>(
            new CommandDefinition(sql, new
                {
                    document_id = documentId
                }, 
                uow.Transaction, 
                cancellationToken: ct));
    }

    public async Task<PmPayableCreditMemoHead> ReadPayableCreditMemoHeadAsync(
        Guid documentId,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
SELECT
    document_id AS DocumentId,
    party_id AS PartyId,
    property_id AS PropertyId,
    charge_type_id AS ChargeTypeId,
    credited_on_utc AS CreditedOnUtc,
    amount AS Amount,
    memo AS Memo
FROM doc_pm_payable_credit_memo
WHERE document_id = @document_id;
""";

        return await uow.Connection.QuerySingleAsync<PmPayableCreditMemoHead>(
            new CommandDefinition(sql, new
                {
                    document_id = documentId
                }, 
                uow.Transaction, 
                cancellationToken: ct));
    }

    public async Task<PmPayableApplyHead> ReadPayableApplyHeadAsync(
        Guid documentId,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
SELECT
    document_id AS DocumentId,
    credit_document_id AS CreditDocumentId,
    charge_document_id AS ChargeDocumentId,
    applied_on_utc AS AppliedOnUtc,
    amount AS Amount,
    memo AS Memo
FROM doc_pm_payable_apply
WHERE document_id = @document_id;
""";

        return await uow.Connection.QuerySingleAsync<PmPayableApplyHead>(
            new CommandDefinition(sql, new
                {
                    document_id = documentId
                }, 
                uow.Transaction, 
                cancellationToken: ct));
    }

    public async Task<PmReceivableApplyHead> ReadReceivableApplyHeadAsync(
        Guid documentId,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
SELECT
    document_id AS DocumentId,
    credit_document_id AS CreditDocumentId,
    charge_document_id AS ChargeDocumentId,
    applied_on_utc AS AppliedOnUtc,
    amount AS Amount,
    memo AS Memo
FROM doc_pm_receivable_apply
WHERE document_id = @document_id;
""";

        return await uow.Connection.QuerySingleAsync<PmReceivableApplyHead>(
            new CommandDefinition(sql, new
                {
                    document_id = documentId
                }, 
                uow.Transaction,
                cancellationToken: ct));
    }

    public async Task<IReadOnlyList<PmReceivableChargeHead>> ReadReceivableChargeHeadsAsync(
        IReadOnlyCollection<Guid> documentIds,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        if (documentIds is null || documentIds.Count == 0)
            return [];

        const string sql = """
SELECT
    rc.document_id AS DocumentId,
    p.party_id AS PartyId,
    l.property_id AS PropertyId,
    rc.lease_id AS LeaseId,
    rc.charge_type_id AS ChargeTypeId,
    rc.due_on_utc AS DueOnUtc,
    rc.amount AS Amount,
    rc.memo AS Memo
FROM doc_pm_receivable_charge rc
JOIN doc_pm_lease l
  ON l.document_id = rc.lease_id
JOIN doc_pm_lease__parties p
  ON p.document_id = l.document_id
 AND p.is_primary = TRUE
WHERE rc.document_id = ANY(@ids);
""";

        var rows = await uow.Connection.QueryAsync<PmReceivableChargeHead>(
            new CommandDefinition(sql, new
                {
                    ids = documentIds.ToArray()
                }, 
                uow.Transaction,
                cancellationToken: ct));

        return rows.AsList();
    }

    public async Task<IReadOnlyList<PmRentChargeHead>> ReadRentChargeHeadsAsync(
        IReadOnlyCollection<Guid> documentIds,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        if (documentIds is null || documentIds.Count == 0)
            return [];

        const string sql = """
SELECT
    rc.document_id AS DocumentId,
    rc.lease_id AS LeaseId,
    p.party_id AS PartyId,
    l.property_id AS PropertyId,
    rc.period_from_utc AS PeriodFromUtc,
    rc.period_to_utc AS PeriodToUtc,
    rc.due_on_utc AS DueOnUtc,
    rc.amount AS Amount,
    rc.memo AS Memo
FROM doc_pm_rent_charge rc
JOIN doc_pm_lease l
  ON l.document_id = rc.lease_id
JOIN doc_pm_lease__parties p
  ON p.document_id = l.document_id
 AND p.is_primary = TRUE
WHERE rc.document_id = ANY(@ids);
""";

        var rows = await uow.Connection.QueryAsync<PmRentChargeHead>(
            new CommandDefinition(sql, new
                {
                    ids = documentIds.ToArray()
                }, 
                uow.Transaction,
                cancellationToken: ct));

        return rows.AsList();
    }

    public async Task<IReadOnlyList<PmLateFeeChargeHead>> ReadLateFeeChargeHeadsAsync(
        IReadOnlyCollection<Guid> documentIds,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        if (documentIds is null || documentIds.Count == 0)
            return [];

        const string sql = """
SELECT
    lfc.document_id AS DocumentId,
    p.party_id AS PartyId,
    l.property_id AS PropertyId,
    lfc.lease_id AS LeaseId,
    lfc.due_on_utc AS DueOnUtc,
    lfc.amount AS Amount,
    lfc.memo AS Memo
FROM doc_pm_late_fee_charge lfc
JOIN doc_pm_lease l
  ON l.document_id = lfc.lease_id
JOIN doc_pm_lease__parties p
  ON p.document_id = l.document_id
 AND p.is_primary = TRUE
WHERE lfc.document_id = ANY(@ids);
""";

        var rows = await uow.Connection.QueryAsync<PmLateFeeChargeHead>(
            new CommandDefinition(sql, new
                {
                    ids = documentIds.ToArray()
                }, 
                uow.Transaction,
                cancellationToken: ct));

        return rows.AsList();
    }

    public async Task<IReadOnlyList<PmReceivablePaymentHead>> ReadReceivablePaymentHeadsAsync(
        IReadOnlyCollection<Guid> documentIds,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        if (documentIds is null || documentIds.Count == 0)
            return [];

        const string sql = """
SELECT
    rp.document_id AS DocumentId,
    p.party_id AS PartyId,
    l.property_id AS PropertyId,
    rp.lease_id AS LeaseId,
    rp.bank_account_id AS BankAccountId,
    rp.received_on_utc AS ReceivedOnUtc,
    rp.amount AS Amount,
    rp.memo AS Memo
FROM doc_pm_receivable_payment rp
JOIN doc_pm_lease l
  ON l.document_id = rp.lease_id
JOIN doc_pm_lease__parties p
  ON p.document_id = l.document_id
 AND p.is_primary = TRUE
WHERE rp.document_id = ANY(@ids);
""";

        var rows = await uow.Connection.QueryAsync<PmReceivablePaymentHead>(
            new CommandDefinition(sql, new
                {
                    ids = documentIds.ToArray()
                }, 
                uow.Transaction,
                cancellationToken: ct));

        return rows.AsList();
    }

    public async Task<IReadOnlyList<PmReceivableCreditMemoHead>> ReadReceivableCreditMemoHeadsAsync(
        IReadOnlyCollection<Guid> documentIds,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        if (documentIds is null || documentIds.Count == 0)
            return [];

        const string sql = """
SELECT
    rcm.document_id AS DocumentId,
    p.party_id AS PartyId,
    l.property_id AS PropertyId,
    rcm.lease_id AS LeaseId,
    rcm.charge_type_id AS ChargeTypeId,
    rcm.credited_on_utc AS CreditedOnUtc,
    rcm.amount AS Amount,
    rcm.memo AS Memo
FROM doc_pm_receivable_credit_memo rcm
JOIN doc_pm_lease l
  ON l.document_id = rcm.lease_id
JOIN doc_pm_lease__parties p
  ON p.document_id = l.document_id
 AND p.is_primary = TRUE
WHERE rcm.document_id = ANY(@ids);
""";

        var rows = await uow.Connection.QueryAsync<PmReceivableCreditMemoHead>(
            new CommandDefinition(sql, new
                {
                    ids = documentIds.ToArray()
                }, 
                uow.Transaction,
                cancellationToken: ct));

        return rows.AsList();
    }

    public async Task<IReadOnlyList<PmPayableChargeHead>> ReadPayableChargeHeadsAsync(
        IReadOnlyCollection<Guid> documentIds,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        if (documentIds is null || documentIds.Count == 0)
            return [];

        const string sql = """
SELECT
    document_id AS DocumentId,
    party_id AS PartyId,
    property_id AS PropertyId,
    charge_type_id AS ChargeTypeId,
    due_on_utc AS DueOnUtc,
    amount AS Amount,
    vendor_invoice_no AS VendorInvoiceNo,
    memo AS Memo
FROM doc_pm_payable_charge
WHERE document_id = ANY(@ids);
""";

        var rows = await uow.Connection.QueryAsync<PmPayableChargeHead>(
            new CommandDefinition(sql, new
                {
                    ids = documentIds.ToArray()
                }, 
                uow.Transaction,
                cancellationToken: ct));

        return rows.AsList();
    }

    public async Task<IReadOnlyList<PmPayablePaymentHead>> ReadPayablePaymentHeadsAsync(
        IReadOnlyCollection<Guid> documentIds,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        if (documentIds is null || documentIds.Count == 0)
            return [];

        const string sql = """
SELECT
    document_id AS DocumentId,
    party_id AS PartyId,
    property_id AS PropertyId,
    bank_account_id AS BankAccountId,
    paid_on_utc AS PaidOnUtc,
    amount AS Amount,
    memo AS Memo
FROM doc_pm_payable_payment
WHERE document_id = ANY(@ids);
""";

        var rows = await uow.Connection.QueryAsync<PmPayablePaymentHead>(
            new CommandDefinition(sql, new
                {
                    ids = documentIds.ToArray()
                }, 
                uow.Transaction,
                cancellationToken: ct));

        return rows.AsList();
    }

    public async Task<IReadOnlyList<PmPayableCreditMemoHead>> ReadPayableCreditMemoHeadsAsync(
        IReadOnlyCollection<Guid> documentIds,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        if (documentIds is null || documentIds.Count == 0)
            return [];

        const string sql = """
SELECT
    document_id AS DocumentId,
    party_id AS PartyId,
    property_id AS PropertyId,
    charge_type_id AS ChargeTypeId,
    credited_on_utc AS CreditedOnUtc,
    amount AS Amount,
    memo AS Memo
FROM doc_pm_payable_credit_memo
WHERE document_id = ANY(@ids);
""";

        var rows = await uow.Connection.QueryAsync<PmPayableCreditMemoHead>(
            new CommandDefinition(sql, new
                {
                    ids = documentIds.ToArray()
                }, 
                uow.Transaction,
                cancellationToken: ct));

        return rows.AsList();
    }

    public async Task<IReadOnlyList<PmReceivableAllocationRead>> ReadActiveReceivableAllocationsAsync(
        Guid partyId,
        Guid propertyId,
        Guid leaseId,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        if (partyId == Guid.Empty || propertyId == Guid.Empty || leaseId == Guid.Empty)
            return [];

        const int postedStatus = 2;

        const string sql = """
SELECT
    a.document_id AS ApplyId,
    apply_head.display AS ApplyDisplay,
    apply_doc.number AS ApplyNumber,
    a.credit_document_id AS CreditDocumentId,
    credit_doc.type_code AS CreditDocumentType,
    COALESCE(payment_head.display, credit_memo_head.display) AS CreditDocumentDisplay,
    credit_doc.number AS CreditDocumentNumber,
    a.charge_document_id AS ChargeDocumentId,
    charge_doc.type_code AS ChargeDocumentType,
    COALESCE(rc_head.display, lfc_head.display, rent_head.display) AS ChargeDisplay,
    charge_doc.number AS ChargeNumber,
    a.applied_on_utc AS AppliedOnUtc,
    a.amount AS Amount,
    TRUE AS IsPosted
FROM doc_pm_receivable_apply a
JOIN documents apply_doc
  ON apply_doc.id = a.document_id
 AND apply_doc.type_code = @apply_type_code
 AND apply_doc.status = @posted_status
JOIN document_relationships rel_payment
  ON rel_payment.from_document_id = a.document_id
 AND rel_payment.to_document_id = a.credit_document_id
 AND rel_payment.relationship_code_norm = 'based_on'
JOIN document_relationships rel_charge
  ON rel_charge.from_document_id = a.document_id
 AND rel_charge.to_document_id = a.charge_document_id
 AND rel_charge.relationship_code_norm = 'based_on'
JOIN doc_pm_receivable_apply apply_head
  ON apply_head.document_id = a.document_id
LEFT JOIN doc_pm_receivable_payment payment_head
  ON payment_head.document_id = a.credit_document_id
LEFT JOIN doc_pm_lease payment_lease
  ON payment_lease.document_id = payment_head.lease_id
LEFT JOIN doc_pm_lease__parties payment_party
  ON payment_party.document_id = payment_lease.document_id
 AND payment_party.is_primary = TRUE
LEFT JOIN doc_pm_receivable_credit_memo credit_memo_head
  ON credit_memo_head.document_id = a.credit_document_id
LEFT JOIN doc_pm_lease credit_memo_lease
  ON credit_memo_lease.document_id = credit_memo_head.lease_id
LEFT JOIN doc_pm_lease__parties credit_memo_party
  ON credit_memo_party.document_id = credit_memo_lease.document_id
 AND credit_memo_party.is_primary = TRUE
JOIN documents credit_doc
  ON credit_doc.id = a.credit_document_id
 AND credit_doc.type_code = ANY(@credit_type_codes)
JOIN documents charge_doc
  ON charge_doc.id = a.charge_document_id
LEFT JOIN doc_pm_receivable_charge rc_head
  ON rc_head.document_id = a.charge_document_id
LEFT JOIN doc_pm_lease rc_lease
  ON rc_lease.document_id = rc_head.lease_id
LEFT JOIN doc_pm_lease__parties rc_party
  ON rc_party.document_id = rc_lease.document_id
 AND rc_party.is_primary = TRUE
LEFT JOIN doc_pm_late_fee_charge lfc_head
  ON lfc_head.document_id = a.charge_document_id
LEFT JOIN doc_pm_lease lfc_lease
  ON lfc_lease.document_id = lfc_head.lease_id
LEFT JOIN doc_pm_lease__parties lfc_party
  ON lfc_party.document_id = lfc_lease.document_id
 AND lfc_party.is_primary = TRUE
LEFT JOIN doc_pm_rent_charge rent_head
  ON rent_head.document_id = a.charge_document_id
LEFT JOIN doc_pm_lease rent_lease
  ON rent_lease.document_id = rent_head.lease_id
LEFT JOIN doc_pm_lease__parties rent_party
  ON rent_party.document_id = rent_lease.document_id
 AND rent_party.is_primary = TRUE
WHERE COALESCE(payment_party.party_id, credit_memo_party.party_id) = @party_id
  AND COALESCE(payment_lease.property_id, credit_memo_lease.property_id) = @property_id
  AND COALESCE(payment_head.lease_id, credit_memo_head.lease_id) = @lease_id
  AND COALESCE(rc_party.party_id, lfc_party.party_id, rent_party.party_id) = @party_id
  AND COALESCE(rc_lease.property_id, lfc_lease.property_id, rent_lease.property_id) = @property_id
  AND COALESCE(rc_head.lease_id, lfc_head.lease_id, rent_head.lease_id) = @lease_id
  AND charge_doc.type_code = ANY(@charge_type_codes)
ORDER BY a.applied_on_utc, a.document_id;
""";

        var rows = await uow.Connection.QueryAsync<PmReceivableAllocationRead>(
            new CommandDefinition(
                sql,
                new
                {
                    apply_type_code = PropertyManagementCodes.ReceivableApply,
                    posted_status = postedStatus,
                    credit_type_codes = new[]
                    {
                        PropertyManagementCodes.ReceivablePayment,
                        PropertyManagementCodes.ReceivableCreditMemo
                    },
                    charge_type_codes = new[]
                    {
                        PropertyManagementCodes.ReceivableCharge,
                        PropertyManagementCodes.LateFeeCharge,
                        PropertyManagementCodes.RentCharge
                    },
                    party_id = partyId,
                    property_id = propertyId,
                    lease_id = leaseId
                },
                uow.Transaction,
                cancellationToken: ct));

        return rows.AsList();
    }

    public async Task<IReadOnlyList<PmPayableAllocationRead>> ReadActivePayableAllocationsAsync(
        Guid partyId,
        Guid propertyId,
        DateOnly? fromMonthInclusive = null,
        DateOnly? toMonthInclusive = null,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        if (partyId == Guid.Empty || propertyId == Guid.Empty)
            return [];

        const int postedStatus = 2;

        const string sql = """
SELECT
    a.document_id AS ApplyId,
    apply_head.display AS ApplyDisplay,
    apply_doc.number AS ApplyNumber,
    a.credit_document_id AS CreditDocumentId,
    credit_doc.type_code AS CreditDocumentType,
    COALESCE(payment_head.display, credit_memo_head.display) AS CreditDocumentDisplay,
    credit_doc.number AS CreditDocumentNumber,
    a.charge_document_id AS ChargeDocumentId,
    charge_doc.type_code AS ChargeDocumentType,
    charge_head.display AS ChargeDisplay,
    charge_doc.number AS ChargeNumber,
    a.applied_on_utc AS AppliedOnUtc,
    a.amount AS Amount,
    TRUE AS IsPosted
FROM doc_pm_payable_apply a
JOIN documents apply_doc
  ON apply_doc.id = a.document_id
 AND apply_doc.type_code = @apply_type_code
 AND apply_doc.status = @posted_status
JOIN document_relationships rel_credit
  ON rel_credit.from_document_id = a.document_id
 AND rel_credit.to_document_id = a.credit_document_id
 AND rel_credit.relationship_code_norm = 'based_on'
JOIN document_relationships rel_charge
  ON rel_charge.from_document_id = a.document_id
 AND rel_charge.to_document_id = a.charge_document_id
 AND rel_charge.relationship_code_norm = 'based_on'
JOIN doc_pm_payable_apply apply_head
  ON apply_head.document_id = a.document_id
JOIN documents credit_doc
  ON credit_doc.id = a.credit_document_id
LEFT JOIN doc_pm_payable_payment payment_head
  ON payment_head.document_id = a.credit_document_id
LEFT JOIN doc_pm_payable_credit_memo credit_memo_head
  ON credit_memo_head.document_id = a.credit_document_id
JOIN documents charge_doc
  ON charge_doc.id = a.charge_document_id
JOIN doc_pm_payable_charge charge_head
  ON charge_head.document_id = a.charge_document_id
WHERE COALESCE(payment_head.party_id, credit_memo_head.party_id) = @party_id
  AND COALESCE(payment_head.property_id, credit_memo_head.property_id) = @property_id
  AND charge_head.party_id = @party_id
  AND charge_head.property_id = @property_id
  AND (@from_month::date IS NULL OR a.applied_on_utc >= @from_month::date)
  AND (@to_month_exclusive::date IS NULL OR a.applied_on_utc < @to_month_exclusive::date)
  AND credit_doc.type_code = ANY(@credit_type_codes)
  AND charge_doc.type_code = @charge_type_code
ORDER BY a.applied_on_utc, a.document_id;
""";

        var toMonthExclusive = toMonthInclusive?.AddMonths(1);

        var rows = await uow.Connection.QueryAsync<PmPayableAllocationRead>(
            new CommandDefinition(
                sql,
                new
                {
                    apply_type_code = PropertyManagementCodes.PayableApply,
                    posted_status = postedStatus,
                    credit_type_codes = new[]
                    {
                        PropertyManagementCodes.PayablePayment,
                        PropertyManagementCodes.PayableCreditMemo
                    },
                    charge_type_code = PropertyManagementCodes.PayableCharge,
                    party_id = partyId,
                    property_id = propertyId,
                    from_month = fromMonthInclusive,
                    to_month_exclusive = toMonthExclusive
                },
                uow.Transaction,
                cancellationToken: ct));

        return rows.AsList();
    }

    public async Task<DateOnly?> ReadFirstPayablesActivityMonthAsync(
        Guid partyId,
        Guid propertyId,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        if (partyId == Guid.Empty || propertyId == Guid.Empty)
            return null;

        const string sql = """
SELECT MIN(x.month_start)
FROM (
    SELECT date_trunc('month', pc.due_on_utc::timestamp)::date AS month_start
    FROM doc_pm_payable_charge pc
    WHERE pc.party_id = @party_id
      AND pc.property_id = @property_id
    UNION ALL
    SELECT date_trunc('month', pp.paid_on_utc::timestamp)::date AS month_start
    FROM doc_pm_payable_payment pp
    WHERE pp.party_id = @party_id
      AND pp.property_id = @property_id
    UNION ALL
    SELECT date_trunc('month', pcm.credited_on_utc::timestamp)::date AS month_start
    FROM doc_pm_payable_credit_memo pcm
    WHERE pcm.party_id = @party_id
      AND pcm.property_id = @property_id
) x;
""";

        return await uow.Connection.ExecuteScalarAsync<DateOnly?>(
            new CommandDefinition(sql, new
                {
                    party_id = partyId,
                    property_id = propertyId
                }, 
                uow.Transaction,
                cancellationToken: ct));
    }

    public async Task<IReadOnlyList<PmChargeTypeHead>> ReadChargeTypeHeadsAsync(
        IReadOnlyCollection<Guid> chargeTypeIds,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        if (chargeTypeIds is null || chargeTypeIds.Count == 0)
            return [];

        const string sql = """
SELECT
    ct.catalog_id AS ChargeTypeId,
    ct.display AS Display,
    ct.credit_account_id AS CreditAccountId
FROM cat_pm_receivable_charge_type ct
JOIN catalogs c ON c.id = ct.catalog_id
WHERE ct.catalog_id = ANY(@ids)
  AND c.catalog_code = @catalog_code
  AND c.is_deleted = FALSE;
""";

        var rows = await uow.Connection.QueryAsync<PmChargeTypeHead>(
            new CommandDefinition(
                sql,
                new
                {
                    ids = chargeTypeIds.ToArray(),
                    catalog_code = PropertyManagementCodes.ReceivableChargeType
                },
                uow.Transaction,
                cancellationToken: ct));

        return rows.AsList();
    }

    public async Task<PmChargeTypeHead> ReadChargeTypeHeadAsync(Guid chargeTypeId, CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
SELECT
    ct.catalog_id AS ChargeTypeId,
    ct.display AS Display,
    ct.credit_account_id AS CreditAccountId
FROM cat_pm_receivable_charge_type ct
JOIN catalogs c ON c.id = ct.catalog_id
WHERE ct.catalog_id = @charge_type_id
  AND c.catalog_code = @catalog_code
  AND c.is_deleted = FALSE;
""";

        return await uow.Connection.QuerySingleAsync<PmChargeTypeHead>(
            new CommandDefinition(
                sql,
                new
                {
                    charge_type_id = chargeTypeId,
                    catalog_code = PropertyManagementCodes.ReceivableChargeType
                },
                uow.Transaction,
                cancellationToken: ct));
    }

    public async Task<IReadOnlyList<PmPayableChargeTypeHead>> ReadPayableChargeTypeHeadsAsync(
        IReadOnlyCollection<Guid> chargeTypeIds,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        if (chargeTypeIds is null || chargeTypeIds.Count == 0)
            return [];

        const string sql = """
SELECT
    ct.catalog_id AS ChargeTypeId,
    ct.display AS Display,
    ct.debit_account_id AS DebitAccountId
FROM cat_pm_payable_charge_type ct
JOIN catalogs c ON c.id = ct.catalog_id
WHERE ct.catalog_id = ANY(@ids)
  AND c.catalog_code = @catalog_code
  AND c.is_deleted = FALSE;
""";

        var rows = await uow.Connection.QueryAsync<PmPayableChargeTypeHead>(
            new CommandDefinition(
                sql,
                new
                {
                    ids = chargeTypeIds.ToArray(),
                    catalog_code = PropertyManagementCodes.PayableChargeType
                },
                uow.Transaction,
                cancellationToken: ct));

        return rows.AsList();
    }

    public async Task<PmPayableChargeTypeHead> ReadPayableChargeTypeHeadAsync(
        Guid chargeTypeId,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
SELECT
    ct.catalog_id AS ChargeTypeId,
    ct.display AS Display,
    ct.debit_account_id AS DebitAccountId
FROM cat_pm_payable_charge_type ct
JOIN catalogs c ON c.id = ct.catalog_id
WHERE ct.catalog_id = @charge_type_id
  AND c.catalog_code = @catalog_code
  AND c.is_deleted = FALSE;
""";

        return await uow.Connection.QuerySingleAsync<PmPayableChargeTypeHead>(
            new CommandDefinition(
                sql,
                new
                {
                    charge_type_id = chargeTypeId,
                    catalog_code = PropertyManagementCodes.PayableChargeType
                },
                uow.Transaction,
                cancellationToken: ct));
    }

    public async Task<IReadOnlyList<PmDocumentInfo>> ReadDocumentInfosAsync(
        IReadOnlyCollection<Guid> documentIds,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        if (documentIds is null || documentIds.Count == 0)
            return [];

        const string sql = """
SELECT
    id AS DocumentId,
    type_code AS TypeCode,
    number AS Number
FROM documents
WHERE id = ANY(@ids);
""";

        var rows = await uow.Connection.QueryAsync<PmDocumentInfo>(
            new CommandDefinition(sql, new
                {
                    ids = documentIds.ToArray()
                }, 
                uow.Transaction,
                cancellationToken: ct));

        return rows.AsList();
    }
}
