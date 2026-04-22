using Dapper;
using NGB.AgencyBilling.Derivations;
using NGB.Persistence.UnitOfWork;

namespace NGB.AgencyBilling.PostgreSql.Derivations;

public sealed class AgencyBillingInvoiceDraftDerivationReader(IUnitOfWork uow)
    : IAgencyBillingInvoiceDraftDerivationReader
{
    public async Task<AgencyBillingInvoiceDraftDefaults?> ResolveDefaultsAsync(
        Guid clientId,
        Guid projectId,
        DateOnly workDate,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
WITH selected_contract AS (
    SELECT
        cc.document_id AS contract_id,
        cc.currency_code AS contract_currency_code,
        cc.payment_terms_id AS contract_payment_terms_id,
        NULLIF(BTRIM(cc.invoice_memo_template), '') AS invoice_memo_template
    FROM doc_ab_client_contract cc
    JOIN documents d
      ON d.id = cc.document_id
    WHERE d.status = 2
      AND cc.is_active = TRUE
      AND cc.client_id = @client_id
      AND cc.project_id = @project_id
      AND cc.effective_from <= @work_date
      AND (cc.effective_to IS NULL OR cc.effective_to >= @work_date)
    ORDER BY cc.effective_from DESC,
             d.date_utc DESC,
             cc.document_id DESC
    LIMIT 1
)
SELECT
    c.contract_id AS ContractId,
    COALESCE(
        NULLIF(BTRIM(c.contract_currency_code), ''),
        NULLIF(BTRIM(cl.default_currency), ''),
        @default_currency) AS CurrencyCode,
    c.invoice_memo_template AS InvoiceMemo,
    COALESCE(contract_terms.due_days, client_terms.due_days, 0) AS DueDays
FROM selected_contract c
LEFT JOIN cat_ab_client cl
  ON cl.catalog_id = @client_id
LEFT JOIN cat_ab_payment_terms contract_terms
  ON contract_terms.catalog_id = c.contract_payment_terms_id
LEFT JOIN cat_ab_payment_terms client_terms
  ON client_terms.catalog_id = cl.payment_terms_id;
""";

        return await uow.Connection.QuerySingleOrDefaultAsync<AgencyBillingInvoiceDraftDefaults>(
            new CommandDefinition(
                sql,
                new
                {
                    client_id = clientId,
                    project_id = projectId,
                    work_date = workDate,
                    default_currency = AgencyBillingCodes.DefaultCurrency
                },
                uow.Transaction,
                cancellationToken: ct));
    }

    public async Task<bool> HasExistingInvoiceForTimesheetAsync(
        Guid sourceTimesheetId,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
SELECT EXISTS (
    SELECT 1
    FROM doc_ab_sales_invoice__lines sil
    JOIN documents d
      ON d.id = sil.document_id
    WHERE sil.source_timesheet_id = @source_timesheet_id
      AND d.status IN (1, 2)
);
""";

        return await uow.Connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(
                sql,
                new { source_timesheet_id = sourceTimesheetId },
                uow.Transaction,
                cancellationToken: ct));
    }
}
