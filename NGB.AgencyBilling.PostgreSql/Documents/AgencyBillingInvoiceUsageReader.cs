using Dapper;
using NGB.AgencyBilling.Documents;
using NGB.Persistence.UnitOfWork;

namespace NGB.AgencyBilling.PostgreSql.Documents;

public sealed class AgencyBillingInvoiceUsageReader(IUnitOfWork uow) : IAgencyBillingInvoiceUsageReader
{
    public async Task<AgencyBillingTimesheetInvoiceUsage> GetPostedInvoiceUsageForTimesheetAsync(
        Guid sourceTimesheetId,
        Guid? excludingSalesInvoiceId = null,
        CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
SELECT
    COALESCE(SUM(line.quantity_hours), 0::numeric(18,4)) AS InvoicedHours,
    COALESCE(SUM(line.line_amount), 0::numeric(18,4)) AS InvoicedAmount
FROM doc_ab_sales_invoice__lines line
JOIN documents d
  ON d.id = line.document_id
WHERE line.source_timesheet_id = @source_timesheet_id
  AND d.status = 2
  AND (@excluding_sales_invoice_id IS NULL OR line.document_id <> @excluding_sales_invoice_id);
""";

        return await uow.Connection.QuerySingleAsync<AgencyBillingTimesheetInvoiceUsage>(
            new CommandDefinition(
                sql,
                new
                {
                    source_timesheet_id = sourceTimesheetId,
                    excluding_sales_invoice_id = excludingSalesInvoiceId
                },
                uow.Transaction,
                cancellationToken: ct));
    }
}
