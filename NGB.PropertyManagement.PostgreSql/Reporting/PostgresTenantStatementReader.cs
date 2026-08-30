using Dapper;
using NGB.Core.Documents;
using NGB.Contracts.Common;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Reporting;
using NGB.Tools.Exceptions;

namespace NGB.PropertyManagement.PostgreSql.Reporting;

public sealed class PostgresTenantStatementReader(IUnitOfWork uow) : ITenantStatementReader
{
    private const string LeaseTypeCode = PropertyManagementCodes.Lease;

    private const string StatementCte = """
WITH lease_validation AS (
    SELECT EXISTS (
        SELECT 1
        FROM documents
        WHERE id = @lease_id
          AND type_code = @lease_type_code
    ) AS lease_valid
),
statement_rows AS (
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

    private static string BuildPageSql(bool knownStats, bool useSeek)
    {
        var statsSql = knownStats
        ? """
,
stats AS (
    SELECT
        @known_total::int AS total_count,
        @known_opening_balance::numeric(18,4) AS opening_balance,
        @known_total_charges::numeric(18,4) AS total_charges,
        @known_total_credits::numeric(18,4) AS total_credits
),
"""
        : """
,
stats AS (
    SELECT
        COUNT(visible.document_id)::int AS total_count,
        opening.opening_balance AS opening_balance,
        COALESCE(SUM(visible.charge_amount), 0)::numeric(18,4) AS total_charges,
        COALESCE(SUM(visible.credit_amount), 0)::numeric(18,4) AS total_credits
    FROM opening_balance opening
    LEFT JOIN visible_rows visible ON TRUE
    GROUP BY opening.opening_balance
),
""";
        var seekRowsSql = useSeek
            ? """
seek_rows AS (
    SELECT *
    FROM visible_rows
    WHERE (occurred_on_utc, sort_order, document_id)
        > (@after_occurred_on_utc::date, @after_sort_order::int, @after_document_id::uuid)
),
"""
            : string.Empty;
        var pageSource = useSeek ? "seek_rows" : "visible_rows";
        var balanceBase = useSeek ? "@known_running_balance::numeric(18,4)" : "opening.opening_balance";
        var openingJoin = useSeek ? string.Empty : "CROSS JOIN opening_balance opening";
        var offsetSql = useSeek ? string.Empty : "OFFSET @offset";

        return StatementCte + statsSql + seekRowsSql + $"""
paged AS (
    SELECT
        visible.occurred_on_utc,
        visible.document_id,
        visible.document_type,
        visible.document_display,
        visible.entry_type_display,
        visible.description,
        visible.sort_order,
        visible.charge_amount,
        visible.credit_amount,
        ({balanceBase}
          + SUM(visible.delta_amount) OVER (
              ORDER BY visible.occurred_on_utc, visible.sort_order, visible.document_id))::numeric(18,4) AS running_balance
    FROM {pageSource} visible
    {openingJoin}
    ORDER BY visible.occurred_on_utc, visible.sort_order, visible.document_id
    {offsetSql}
    LIMIT @limit
)
SELECT
    paged.occurred_on_utc AS OccurredOnUtc,
    paged.document_id AS DocumentId,
    paged.document_type AS DocumentType,
    paged.document_display AS DocumentDisplay,
    paged.entry_type_display AS EntryTypeDisplay,
    paged.description AS Description,
    paged.sort_order AS SortOrder,
    COALESCE(paged.charge_amount, 0) AS ChargeAmount,
    COALESCE(paged.credit_amount, 0) AS CreditAmount,
    COALESCE(paged.running_balance, stats.opening_balance) AS RunningBalance,
    (paged.document_id IS NOT NULL) AS HasRow,
    stats.total_count AS TotalCount,
    stats.opening_balance AS OpeningBalance,
    stats.total_charges AS TotalCharges,
    stats.total_credits AS TotalCredits,
    lease_validation.lease_valid AS LeaseValid
FROM stats
CROSS JOIN lease_validation
LEFT JOIN paged ON TRUE
ORDER BY paged.occurred_on_utc, paged.sort_order, paged.document_id;
""";
    }

    public async Task<TenantStatementPage> GetPageAsync(TenantStatementQuery query, CancellationToken ct = default)
        => await GetPageCoreAsync(query, null, false, ct);

    public async Task<TenantStatementPage> GetCursorPageAsync(
        TenantStatementQuery query,
        TenantStatementPageCursor? cursor,
        CancellationToken ct = default)
        => await GetPageCoreAsync(query with { Offset = cursor?.Offset ?? 0 }, cursor, true, ct);

    private async Task<TenantStatementPage> GetPageCoreAsync(
        TenantStatementQuery query,
        TenantStatementPageCursor? cursor,
        bool cursorPaging,
        CancellationToken ct)
    {
        query.EnsureInvariant();
        await uow.EnsureConnectionOpenAsync(ct);

        var useSeek = cursor is
        {
            AfterOccurredOnUtc: not null,
            AfterSortOrder: not null,
            AfterDocumentId: not null,
            RunningBalance: not null
        };

        var parameters = new
        {
            lease_id = query.LeaseId,
            lease_type_code = LeaseTypeCode,
            from_utc = query.FromUtc,
            to_utc = query.ToUtc,
            posted = (int)DocumentStatus.Posted,
            offset = PagingLimits.BoundOffset(query.Offset),
            limit = cursorPaging && query.Limit < int.MaxValue ? query.Limit + 1 : query.Limit,
            known_total = cursor?.Total,
            known_opening_balance = cursor?.Totals.OpeningBalance,
            known_total_charges = cursor?.Totals.TotalCharges,
            known_total_credits = cursor?.Totals.TotalCredits,
            after_occurred_on_utc = cursor?.AfterOccurredOnUtc,
            after_sort_order = cursor?.AfterSortOrder,
            after_document_id = cursor?.AfterDocumentId,
            known_running_balance = cursor?.RunningBalance
        };

        var dbRows = (await uow.Connection.QueryAsync<CombinedRow>(new CommandDefinition(
            BuildPageSql(cursor is not null, useSeek),
            parameters,
            transaction: uow.Transaction,
            cancellationToken: ct))).AsList();
        var stats = dbRows[0];

        if (!stats.LeaseValid)
            throw new NgbArgumentInvalidException("leaseId", "Select a valid Lease.");

        var dataRows = dbRows.Where(static row => row.HasRow).ToArray();
        var hasMore = cursorPaging && dataRows.Length > query.Limit;
        var visibleRows = dataRows.Take(query.Limit).ToArray();
        var rows = visibleRows
            .Select(MapRow)
            .ToArray();

        var totals = new TenantStatementTotals(
            FromUtc: query.FromUtc,
            ToUtc: query.ToUtc,
            OpeningBalance: stats.OpeningBalance,
            TotalCharges: stats.TotalCharges,
            TotalCredits: stats.TotalCredits,
            ClosingBalance: stats.OpeningBalance + stats.TotalCharges - stats.TotalCredits);
        totals.EnsureInvariant();

        var last = visibleRows.LastOrDefault();
        var page = new TenantStatementPage(
            rows,
            stats.TotalCount,
            totals,
            hasMore,
            last?.OccurredOnUtc,
            last?.SortOrder,
            last?.DocumentId,
            last?.RunningBalance);
        page.EnsureInvariant();

        return page;
    }

    private static TenantStatementRow MapRow(CombinedRow row)
    {
        var result = new TenantStatementRow(
            OccurredOnUtc: row.OccurredOnUtc!.Value,
            DocumentId: row.DocumentId!.Value,
            DocumentType: row.DocumentType!,
            DocumentDisplay: row.DocumentDisplay!,
            EntryTypeDisplay: row.EntryTypeDisplay!,
            Description: row.Description,
            ChargeAmount: row.ChargeAmount,
            CreditAmount: row.CreditAmount,
            RunningBalance: row.RunningBalance);
        result.EnsureInvariant();
        return result;
    }

    private sealed record CombinedRow(
        DateOnly? OccurredOnUtc,
        Guid? DocumentId,
        string? DocumentType,
        string? DocumentDisplay,
        string? EntryTypeDisplay,
        string? Description,
        int? SortOrder,
        decimal ChargeAmount,
        decimal CreditAmount,
        decimal RunningBalance,
        bool HasRow,
        int TotalCount,
        decimal OpeningBalance,
        decimal TotalCharges,
        decimal TotalCredits,
        bool LeaseValid);
}
