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
    COALESCE(SUM(line.quantity_hours) FILTER (WHERE d.id IS NOT NULL), 0::numeric(18,4)) AS InvoicedHours,
    COALESCE(SUM(line.line_amount) FILTER (WHERE d.id IS NOT NULL), 0::numeric(18,4)) AS InvoicedAmount
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

    public async Task<IReadOnlyDictionary<Guid, AgencyBillingTimesheetInvoiceUsage>> GetPostedInvoiceUsageForTimesheetsAsync(
        IReadOnlyCollection<Guid> sourceTimesheetIds,
        Guid? excludingSalesInvoiceId = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sourceTimesheetIds);

        var ids = sourceTimesheetIds.Where(static id => id != Guid.Empty).Distinct().ToArray();
        if (ids.Length == 0)
            return new Dictionary<Guid, AgencyBillingTimesheetInvoiceUsage>();

        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
SELECT
    requested.source_timesheet_id AS SourceTimesheetId,
    COALESCE(SUM(line.quantity_hours) FILTER (WHERE d.id IS NOT NULL), 0::numeric(18,4)) AS InvoicedHours,
    COALESCE(SUM(line.line_amount) FILTER (WHERE d.id IS NOT NULL), 0::numeric(18,4)) AS InvoicedAmount
FROM UNNEST(@source_timesheet_ids::uuid[]) AS requested(source_timesheet_id)
LEFT JOIN doc_ab_sales_invoice__lines line
  ON line.source_timesheet_id = requested.source_timesheet_id
LEFT JOIN documents d
  ON d.id = line.document_id
 AND d.status = 2
 AND (@excluding_sales_invoice_id IS NULL OR line.document_id <> @excluding_sales_invoice_id)
GROUP BY requested.source_timesheet_id;
""";

        var rows = await uow.Connection.QueryAsync<InvoiceUsageRow>(
            new CommandDefinition(
                sql,
                new
                {
                    source_timesheet_ids = ids,
                    excluding_sales_invoice_id = excludingSalesInvoiceId
                },
                uow.Transaction,
                cancellationToken: ct));

        return rows.ToDictionary(
            static row => row.SourceTimesheetId,
            static row => new AgencyBillingTimesheetInvoiceUsage(row.InvoicedHours, row.InvoicedAmount));
    }

    private sealed record InvoiceUsageRow(Guid SourceTimesheetId, decimal InvoicedHours, decimal InvoicedAmount);
}
