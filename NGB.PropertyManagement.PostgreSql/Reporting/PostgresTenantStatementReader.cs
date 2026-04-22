using Dapper;
using NGB.Core.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Reporting;
using NGB.Tools.Exceptions;

namespace NGB.PropertyManagement.PostgreSql.Reporting;

public sealed class PostgresTenantStatementReader(IUnitOfWork uow) : ITenantStatementReader
{
    private const string LeaseTypeCode = PropertyManagementCodes.Lease;

    private const string StatementCte = """
WITH statement_rows AS (
    SELECT
        rc.due_on_utc AS occurred_on_utc,
        rc.document_id AS document_id,
        'pm.rent_charge'::text AS document_type,
        COALESCE(NULLIF(BTRIM(rc.display), ''), '[Rent Charge]') AS document_display,
        'Rent charge'::text AS entry_type_display,
        COALESCE(NULLIF(BTRIM(rc.memo), ''), 'Rent') AS description,
        rc.amount AS charge_amount,
        0::numeric(18,4) AS credit_amount,
        rc.amount AS delta_amount,
        10::int AS sort_order
    FROM doc_pm_rent_charge rc
    JOIN documents d
      ON d.id = rc.document_id
     AND d.status = @posted
    WHERE rc.lease_id = @lease_id
      AND rc.due_on_utc <= @to_utc

    UNION ALL

    SELECT
        ch.due_on_utc AS occurred_on_utc,
        ch.document_id AS document_id,
        'pm.receivable_charge'::text AS document_type,
        COALESCE(NULLIF(BTRIM(ch.display), ''), '[Receivable Charge]') AS document_display,
        'Charge'::text AS entry_type_display,
        COALESCE(NULLIF(BTRIM(ct.display), ''), NULLIF(BTRIM(ch.memo), ''), 'Charge') AS description,
        ch.amount AS charge_amount,
        0::numeric(18,4) AS credit_amount,
        ch.amount AS delta_amount,
        20::int AS sort_order
    FROM doc_pm_receivable_charge ch
    JOIN documents d
      ON d.id = ch.document_id
     AND d.status = @posted
    LEFT JOIN cat_pm_receivable_charge_type ct
      ON ct.catalog_id = ch.charge_type_id
    WHERE ch.lease_id = @lease_id
      AND ch.due_on_utc <= @to_utc

    UNION ALL

    SELECT
        lf.due_on_utc AS occurred_on_utc,
        lf.document_id AS document_id,
        'pm.late_fee_charge'::text AS document_type,
        COALESCE(NULLIF(BTRIM(lf.display), ''), '[Late Fee Charge]') AS document_display,
        'Late fee'::text AS entry_type_display,
        COALESCE(NULLIF(BTRIM(lf.memo), ''), 'Late Fee') AS description,
        lf.amount AS charge_amount,
        0::numeric(18,4) AS credit_amount,
        lf.amount AS delta_amount,
        30::int AS sort_order
    FROM doc_pm_late_fee_charge lf
    JOIN documents d
      ON d.id = lf.document_id
     AND d.status = @posted
    WHERE lf.lease_id = @lease_id
      AND lf.due_on_utc <= @to_utc

    UNION ALL

    SELECT
        p.received_on_utc AS occurred_on_utc,
        p.document_id AS document_id,
        'pm.receivable_payment'::text AS document_type,
        COALESCE(NULLIF(BTRIM(p.display), ''), '[Receivable Payment]') AS document_display,
        'Payment'::text AS entry_type_display,
        COALESCE(NULLIF(BTRIM(p.memo), ''), 'Payment') AS description,
        0::numeric(18,4) AS charge_amount,
        p.amount AS credit_amount,
        -p.amount AS delta_amount,
        40::int AS sort_order
    FROM doc_pm_receivable_payment p
    JOIN documents d
      ON d.id = p.document_id
     AND d.status = @posted
    WHERE p.lease_id = @lease_id
      AND p.received_on_utc <= @to_utc

    UNION ALL

    SELECT
        cm.credited_on_utc AS occurred_on_utc,
        cm.document_id AS document_id,
        'pm.receivable_credit_memo'::text AS document_type,
        COALESCE(NULLIF(BTRIM(cm.display), ''), '[Receivable Credit Memo]') AS document_display,
        'Credit memo'::text AS entry_type_display,
        COALESCE(NULLIF(BTRIM(ct.display), ''), NULLIF(BTRIM(cm.memo), ''), 'Credit Memo') AS description,
        0::numeric(18,4) AS charge_amount,
        cm.amount AS credit_amount,
        -cm.amount AS delta_amount,
        50::int AS sort_order
    FROM doc_pm_receivable_credit_memo cm
    JOIN documents d
      ON d.id = cm.document_id
     AND d.status = @posted
    LEFT JOIN cat_pm_receivable_charge_type ct
      ON ct.catalog_id = cm.charge_type_id
    WHERE cm.lease_id = @lease_id
      AND cm.credited_on_utc <= @to_utc

    UNION ALL

    SELECT
        rp.returned_on_utc AS occurred_on_utc,
        rp.document_id AS document_id,
        'pm.receivable_returned_payment'::text AS document_type,
        COALESCE(NULLIF(BTRIM(rp.display), ''), '[Receivable Returned Payment]') AS document_display,
        'Returned payment'::text AS entry_type_display,
        COALESCE(NULLIF(BTRIM(rp.memo), ''), 'Returned Payment') AS description,
        rp.amount AS charge_amount,
        0::numeric(18,4) AS credit_amount,
        rp.amount AS delta_amount,
        60::int AS sort_order
    FROM doc_pm_receivable_returned_payment rp
    JOIN documents d
      ON d.id = rp.document_id
     AND d.status = @posted
    JOIN doc_pm_receivable_payment p
      ON p.document_id = rp.original_payment_id
    WHERE p.lease_id = @lease_id
      AND rp.returned_on_utc <= @to_utc
),
opening_balance AS (
    SELECT COALESCE(SUM(delta_amount), 0)::numeric(18,4) AS opening_balance
    FROM statement_rows
    WHERE @from_utc::date IS NOT NULL
      AND occurred_on_utc < @from_utc::date
),
visible_rows AS (
    SELECT *
    FROM statement_rows
    WHERE @from_utc::date IS NULL
       OR occurred_on_utc >= @from_utc::date
)
""";

    private const string CountSql = StatementCte + """
SELECT COUNT(*)::int
FROM visible_rows;
""";

    private const string TotalsSql = StatementCte + """
SELECT
    (SELECT opening_balance FROM opening_balance) AS OpeningBalance,
    COALESCE((SELECT SUM(charge_amount) FROM visible_rows), 0)::numeric(18,4) AS TotalCharges,
    COALESCE((SELECT SUM(credit_amount) FROM visible_rows), 0)::numeric(18,4) AS TotalCredits;
""";

    private const string PageSql = StatementCte + """
SELECT
    occurred_on_utc AS OccurredOnUtc,
    document_id AS DocumentId,
    document_type AS DocumentType,
    document_display AS DocumentDisplay,
    entry_type_display AS EntryTypeDisplay,
    description AS Description,
    charge_amount AS ChargeAmount,
    credit_amount AS CreditAmount,
    ((SELECT opening_balance FROM opening_balance)
      + SUM(delta_amount) OVER (ORDER BY occurred_on_utc, sort_order, document_id))::numeric(18,4) AS RunningBalance
FROM visible_rows
ORDER BY occurred_on_utc, sort_order, document_id
OFFSET @offset
LIMIT @limit;
""";

    public async Task<TenantStatementPage> GetPageAsync(TenantStatementQuery query, CancellationToken ct = default)
    {
        query.EnsureInvariant();
        await uow.EnsureConnectionOpenAsync(ct);

        await ValidateLeaseFilterAsync(query.LeaseId, ct);

        var parameters = new
        {
            lease_id = query.LeaseId,
            from_utc = query.FromUtc,
            to_utc = query.ToUtc,
            posted = (int)DocumentStatus.Posted,
            offset = query.Offset,
            limit = query.Limit
        };

        var total = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
            CountSql,
            parameters,
            transaction: uow.Transaction,
            cancellationToken: ct));

        var totalsRow = await uow.Connection.QuerySingleAsync<TotalsRow>(new CommandDefinition(
            TotalsSql,
            parameters,
            transaction: uow.Transaction,
            cancellationToken: ct));

        IReadOnlyList<TenantStatementRow> rows;
        if (query.Offset >= total)
        {
            rows = [];
        }
        else
        {
            var dbRows = await uow.Connection.QueryAsync<PageRow>(new CommandDefinition(
                PageSql,
                parameters,
                transaction: uow.Transaction,
                cancellationToken: ct));

            rows = dbRows.Select(MapRow).ToArray();
        }

        var totals = new TenantStatementTotals(
            FromUtc: query.FromUtc,
            ToUtc: query.ToUtc,
            OpeningBalance: totalsRow.OpeningBalance,
            TotalCharges: totalsRow.TotalCharges,
            TotalCredits: totalsRow.TotalCredits,
            ClosingBalance: totalsRow.OpeningBalance + totalsRow.TotalCharges - totalsRow.TotalCredits);
        totals.EnsureInvariant();

        var page = new TenantStatementPage(rows, total, totals);
        page.EnsureInvariant();
        return page;
    }

    private static TenantStatementRow MapRow(PageRow row)
    {
        var result = new TenantStatementRow(
            OccurredOnUtc: row.OccurredOnUtc,
            DocumentId: row.DocumentId,
            DocumentType: row.DocumentType,
            DocumentDisplay: row.DocumentDisplay,
            EntryTypeDisplay: row.EntryTypeDisplay,
            Description: row.Description,
            ChargeAmount: row.ChargeAmount,
            CreditAmount: row.CreditAmount,
            RunningBalance: row.RunningBalance);
        result.EnsureInvariant();
        return result;
    }

    private async Task ValidateLeaseFilterAsync(Guid leaseId, CancellationToken ct)
    {
        if (leaseId == Guid.Empty)
            throw new NgbArgumentInvalidException(nameof(leaseId), "Select a valid Lease.");

        const string sql = """
SELECT 1
FROM documents
WHERE id = @lease_id
  AND type_code = @lease_type_code;
""";

        var exists = await uow.Connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
            sql,
            new { lease_id = leaseId, lease_type_code = LeaseTypeCode },
            transaction: uow.Transaction,
            cancellationToken: ct));

        if (exists is null)
            throw new NgbArgumentInvalidException(nameof(leaseId), "Select a valid Lease.");
    }

    private sealed record TotalsRow(decimal OpeningBalance, decimal TotalCharges, decimal TotalCredits);

    private sealed record PageRow(
        DateOnly OccurredOnUtc,
        Guid DocumentId,
        string DocumentType,
        string DocumentDisplay,
        string EntryTypeDisplay,
        string? Description,
        decimal ChargeAmount,
        decimal CreditAmount,
        decimal RunningBalance);
}
