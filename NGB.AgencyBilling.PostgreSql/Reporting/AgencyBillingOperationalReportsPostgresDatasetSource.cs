using NGB.PostgreSql.Reporting;

namespace NGB.AgencyBilling.PostgreSql.Reporting;

public sealed class AgencyBillingOperationalReportsPostgresDatasetSource : IPostgresReportDatasetSource
{
    public IReadOnlyList<PostgresReportDatasetBinding> GetDatasets()
        =>
        [
            new(
                datasetCode: AgencyBillingCodes.UnbilledTimeReport,
                fromSql: """
                         (
                             WITH invoice_usage AS (
                                 SELECT
                                     line.source_timesheet_id AS timesheet_id,
                                     SUM(line.quantity_hours) AS invoiced_hours,
                                     SUM(line.line_amount) AS invoiced_amount
                                 FROM doc_ab_sales_invoice__lines line
                                 JOIN documents invoice_doc
                                   ON invoice_doc.id = line.document_id
                                 WHERE invoice_doc.status = 2
                                   AND line.source_timesheet_id IS NOT NULL
                                 GROUP BY line.source_timesheet_id
                             )
                             SELECT
                                 ts.document_id AS timesheet_id,
                                 ts.work_date AS work_date,
                                 ts.client_id AS client_id,
                                 COALESCE(c.display, ts.client_id::text) AS client_display,
                                 ts.project_id AS project_id,
                                 COALESCE(p.display, ts.project_id::text) AS project_display,
                                 ts.team_member_id AS team_member_id,
                                 COALESCE(tm.display, ts.team_member_id::text) AS team_member_display,
                                 COALESCE(ts.display, d.number, LEFT(ts.document_id::text, 8)) AS timesheet_display,
                                 trim_scale(GREATEST(ts.total_hours - COALESCE(invoice_usage.invoiced_hours, 0::numeric(18,4)), 0::numeric(18,4))) AS hours_open,
                                 trim_scale(GREATEST(ts.amount - COALESCE(invoice_usage.invoiced_amount, 0::numeric(18,4)), 0::numeric(18,4))) AS amount_open
                             FROM doc_ab_timesheet ts
                             JOIN documents d
                               ON d.id = ts.document_id
                             LEFT JOIN invoice_usage
                               ON invoice_usage.timesheet_id = ts.document_id
                             LEFT JOIN cat_ab_client c
                               ON c.catalog_id = ts.client_id
                             LEFT JOIN cat_ab_project p
                               ON p.catalog_id = ts.project_id
                             LEFT JOIN cat_ab_team_member tm
                               ON tm.catalog_id = ts.team_member_id
                             WHERE d.status = 2
                               AND ts.work_date <= CAST(@as_of_utc AS date)
                         ) x
                         """,
                baseWhereSql: "x.hours_open <> 0 OR x.amount_open <> 0",
                fields:
                [
                    DateField("work_date", "x.work_date"),
                    Field("client_id", "x.client_id", "uuid"),
                    Field("client_display", "x.client_display", "string"),
                    Field("project_id", "x.project_id", "uuid"),
                    Field("project_display", "x.project_display", "string"),
                    Field("team_member_id", "x.team_member_id", "uuid"),
                    Field("team_member_display", "x.team_member_display", "string"),
                    Field("timesheet_id", "x.timesheet_id", "uuid"),
                    Field("timesheet_display", "x.timesheet_display", "string")
                ],
                measures:
                [
                    Measure("hours_open", "x.hours_open"),
                    Measure("amount_open", "x.amount_open")
                ]),
            new(
                datasetCode: AgencyBillingCodes.ProjectProfitabilityReport,
                fromSql: """
                         (
                             WITH timesheet_heads AS (
                                 SELECT
                                     t.project_id,
                                     t.client_id,
                                     SUM(t.total_hours) AS total_hours,
                                     SUM(t.amount) AS delivery_value_amount,
                                     SUM(t.cost_amount) AS cost_amount
                                 FROM doc_ab_timesheet t
                                 JOIN documents d
                                   ON d.id = t.document_id
                                 WHERE d.status = 2
                                   AND t.work_date <= CAST(@as_of_utc AS date)
                                 GROUP BY t.project_id, t.client_id
                             ),
                             billable_hours AS (
                                 SELECT
                                     t.project_id,
                                     SUM(CASE WHEN line.billable THEN line.hours ELSE 0::numeric(18,4) END) AS billable_hours
                                 FROM doc_ab_timesheet t
                                 JOIN documents d
                                   ON d.id = t.document_id
                                 JOIN doc_ab_timesheet__lines line
                                   ON line.document_id = t.document_id
                                 WHERE d.status = 2
                                   AND t.work_date <= CAST(@as_of_utc AS date)
                                 GROUP BY t.project_id
                             ),
                             invoices AS (
                                 SELECT
                                     si.project_id,
                                     si.client_id,
                                     SUM(si.amount) AS billed_amount
                                 FROM doc_ab_sales_invoice si
                                 JOIN documents d
                                   ON d.id = si.document_id
                                 WHERE d.status = 2
                                   AND si.document_date_utc <= CAST(@as_of_utc AS date)
                                 GROUP BY si.project_id, si.client_id
                             ),
                             payments AS (
                                 SELECT
                                     si.project_id,
                                     si.client_id,
                                     SUM(ap.applied_amount) AS collected_amount
                                 FROM doc_ab_customer_payment__applies ap
                                 JOIN doc_ab_customer_payment cp
                                   ON cp.document_id = ap.document_id
                                 JOIN documents payment_doc
                                   ON payment_doc.id = cp.document_id
                                 JOIN doc_ab_sales_invoice si
                                   ON si.document_id = ap.sales_invoice_id
                                 WHERE payment_doc.status = 2
                                   AND cp.document_date_utc <= CAST(@as_of_utc AS date)
                                 GROUP BY si.project_id, si.client_id
                             )
                             SELECT
                                 p.catalog_id AS project_id,
                                 COALESCE(p.display, p.catalog_id::text) AS project_display,
                                 p.client_id AS client_id,
                                 COALESCE(c.display, p.client_id::text) AS client_display,
                                 p.project_manager_id AS project_manager_id,
                                 COALESCE(pm.display, p.project_manager_id::text) AS project_manager_display,
                                 CASE p.status
                                     WHEN 1 THEN 'Planned'
                                     WHEN 2 THEN 'Active'
                                     WHEN 3 THEN 'Completed'
                                     WHEN 4 THEN 'On Hold'
                                     ELSE p.status::text
                                 END AS project_status,
                                 CASE p.billing_model
                                     WHEN 1 THEN 'Time & Materials'
                                     ELSE p.billing_model::text
                                 END AS billing_model_display,
                                 trim_scale(COALESCE(th.total_hours, 0::numeric(18,4))) AS total_hours,
                                 trim_scale(COALESCE(bh.billable_hours, 0::numeric(18,4))) AS billable_hours,
                                 trim_scale(COALESCE(th.delivery_value_amount, 0::numeric(18,4))) AS delivery_value_amount,
                                 trim_scale(COALESCE(th.cost_amount, 0::numeric(18,4))) AS cost_amount,
                                 trim_scale(COALESCE(p.budget_hours, 0::numeric(18,4))) AS budget_hours,
                                 trim_scale(COALESCE(p.budget_amount, 0::numeric(18,4))) AS budget_amount,
                                 trim_scale(COALESCE(inv.billed_amount, 0::numeric(18,4))) AS billed_amount,
                                 trim_scale(COALESCE(pay.collected_amount, 0::numeric(18,4))) AS collected_amount,
                                 trim_scale(COALESCE(inv.billed_amount, 0::numeric(18,4)) - COALESCE(pay.collected_amount, 0::numeric(18,4))) AS outstanding_ar_amount,
                                 trim_scale(COALESCE(inv.billed_amount, 0::numeric(18,4)) - COALESCE(th.cost_amount, 0::numeric(18,4))) AS gross_margin_amount
                             FROM cat_ab_project p
                             LEFT JOIN cat_ab_client c
                               ON c.catalog_id = p.client_id
                             LEFT JOIN cat_ab_team_member pm
                               ON pm.catalog_id = p.project_manager_id
                             LEFT JOIN timesheet_heads th
                               ON th.project_id = p.catalog_id
                              AND th.client_id = p.client_id
                             LEFT JOIN billable_hours bh
                               ON bh.project_id = p.catalog_id
                             LEFT JOIN invoices inv
                               ON inv.project_id = p.catalog_id
                              AND inv.client_id = p.client_id
                             LEFT JOIN payments pay
                               ON pay.project_id = p.catalog_id
                              AND pay.client_id = p.client_id
                         ) x
                         """,
                fields:
                [
                    Field("client_id", "x.client_id", "uuid"),
                    Field("client_display", "x.client_display", "string"),
                    Field("project_id", "x.project_id", "uuid"),
                    Field("project_display", "x.project_display", "string"),
                    Field("project_manager_id", "x.project_manager_id", "uuid"),
                    Field("project_manager_display", "x.project_manager_display", "string"),
                    Field("project_status", "x.project_status", "string"),
                    Field("billing_model_display", "x.billing_model_display", "string")
                ],
                measures:
                [
                    Measure("total_hours", "x.total_hours"),
                    Measure("billable_hours", "x.billable_hours"),
                    Measure("delivery_value_amount", "x.delivery_value_amount"),
                    Measure("cost_amount", "x.cost_amount"),
                    Measure("budget_hours", "x.budget_hours"),
                    Measure("budget_amount", "x.budget_amount"),
                    Measure("billed_amount", "x.billed_amount"),
                    Measure("collected_amount", "x.collected_amount"),
                    Measure("outstanding_ar_amount", "x.outstanding_ar_amount"),
                    Measure("gross_margin_amount", "x.gross_margin_amount")
                ]),
            new(
                datasetCode: AgencyBillingCodes.InvoiceRegisterReport,
                fromSql: """
                         (
                             WITH applied AS (
                                 SELECT
                                     ap.sales_invoice_id AS invoice_id,
                                     SUM(ap.applied_amount) AS applied_amount
                                 FROM doc_ab_customer_payment__applies ap
                                 JOIN doc_ab_customer_payment cp
                                   ON cp.document_id = ap.document_id
                                 JOIN documents payment_doc
                                   ON payment_doc.id = cp.document_id
                                 WHERE payment_doc.status = 2
                                 GROUP BY ap.sales_invoice_id
                             )
                             SELECT
                                 si.document_id AS invoice_id,
                                 si.document_date_utc AS invoice_date,
                                 si.due_date AS due_date,
                                 si.client_id AS client_id,
                                 COALESCE(c.display, si.client_id::text) AS client_display,
                                 si.project_id AS project_id,
                                 COALESCE(p.display, si.project_id::text) AS project_display,
                                 si.contract_id AS contract_id,
                                 COALESCE(cc.display, si.contract_id::text) AS contract_display,
                                 COALESCE(si.display, d.number, LEFT(si.document_id::text, 8)) AS invoice_display,
                                 si.currency_code AS currency_code,
                                 CASE
                                     WHEN COALESCE(applied.applied_amount, 0::numeric(18,4)) >= si.amount THEN 'Paid'
                                     WHEN COALESCE(applied.applied_amount, 0::numeric(18,4)) > 0::numeric(18,4) THEN 'Partially Paid'
                                     ELSE 'Open'
                                 END AS payment_status,
                                 trim_scale(si.amount) AS invoice_amount,
                                 trim_scale(COALESCE(applied.applied_amount, 0::numeric(18,4))) AS applied_amount,
                                 trim_scale(GREATEST(si.amount - COALESCE(applied.applied_amount, 0::numeric(18,4)), 0::numeric(18,4))) AS balance_amount
                             FROM doc_ab_sales_invoice si
                             JOIN documents d
                               ON d.id = si.document_id
                             LEFT JOIN applied
                               ON applied.invoice_id = si.document_id
                             LEFT JOIN cat_ab_client c
                               ON c.catalog_id = si.client_id
                             LEFT JOIN cat_ab_project p
                               ON p.catalog_id = si.project_id
                             LEFT JOIN doc_ab_client_contract cc
                               ON cc.document_id = si.contract_id
                             WHERE d.status = 2
                               AND si.document_date_utc >= CAST(@from_utc AS date)
                               AND si.document_date_utc < CAST(@to_utc_exclusive AS date)
                         ) x
                         """,
                fields:
                [
                    DateField("invoice_date", "x.invoice_date"),
                    DateField("due_date", "x.due_date"),
                    Field("client_id", "x.client_id", "uuid"),
                    Field("client_display", "x.client_display", "string"),
                    Field("project_id", "x.project_id", "uuid"),
                    Field("project_display", "x.project_display", "string"),
                    Field("contract_id", "x.contract_id", "uuid"),
                    Field("contract_display", "x.contract_display", "string"),
                    Field("invoice_id", "x.invoice_id", "uuid"),
                    Field("invoice_display", "x.invoice_display", "string"),
                    Field("currency_code", "x.currency_code", "string"),
                    Field("payment_status", "x.payment_status", "string")
                ],
                measures:
                [
                    Measure("invoice_amount", "x.invoice_amount"),
                    Measure("applied_amount", "x.applied_amount"),
                    Measure("balance_amount", "x.balance_amount")
                ]),
            new(
                datasetCode: AgencyBillingCodes.ArAgingReport,
                fromSql: """
                         (
                             WITH applied AS (
                                 SELECT
                                     ap.sales_invoice_id AS invoice_id,
                                     SUM(ap.applied_amount) AS applied_amount
                                 FROM doc_ab_customer_payment__applies ap
                                 JOIN doc_ab_customer_payment cp
                                   ON cp.document_id = ap.document_id
                                 JOIN documents payment_doc
                                   ON payment_doc.id = cp.document_id
                                 WHERE payment_doc.status = 2
                                   AND cp.document_date_utc <= CAST(@as_of_utc AS date)
                                 GROUP BY ap.sales_invoice_id
                             ),
                             open_items AS (
                                 SELECT
                                     si.document_id AS invoice_id,
                                     si.document_date_utc AS invoice_date,
                                     si.due_date AS due_date,
                                     si.client_id AS client_id,
                                     si.project_id AS project_id,
                                     GREATEST(si.amount - COALESCE(applied.applied_amount, 0::numeric(18,4)), 0::numeric(18,4)) AS open_amount
                                 FROM doc_ab_sales_invoice si
                                 JOIN documents d
                                   ON d.id = si.document_id
                                 LEFT JOIN applied
                                   ON applied.invoice_id = si.document_id
                                 WHERE d.status = 2
                                   AND si.document_date_utc <= CAST(@as_of_utc AS date)
                             )
                             SELECT
                                 oi.invoice_id AS invoice_id,
                                 oi.invoice_date AS invoice_date,
                                 oi.due_date AS due_date,
                                 oi.client_id AS client_id,
                                 COALESCE(c.display, oi.client_id::text) AS client_display,
                                 oi.project_id AS project_id,
                                 COALESCE(p.display, oi.project_id::text) AS project_display,
                                 COALESCE(si.display, d.number, LEFT(oi.invoice_id::text, 8)) AS invoice_display,
                                 GREATEST(CAST(CAST(@as_of_utc AS date) - oi.due_date AS integer), 0) AS days_past_due,
                                 CASE
                                     WHEN oi.due_date >= CAST(@as_of_utc AS date) THEN 'Current'
                                     WHEN CAST(@as_of_utc AS date) - oi.due_date BETWEEN 1 AND 30 THEN '1-30'
                                     WHEN CAST(@as_of_utc AS date) - oi.due_date BETWEEN 31 AND 60 THEN '31-60'
                                     WHEN CAST(@as_of_utc AS date) - oi.due_date BETWEEN 61 AND 90 THEN '61-90'
                                     ELSE '90+'
                                 END AS aging_bucket,
                                 trim_scale(oi.open_amount) AS open_amount,
                                 trim_scale(CASE WHEN oi.due_date >= CAST(@as_of_utc AS date) THEN oi.open_amount ELSE 0::numeric(18,4) END) AS current_amount,
                                 trim_scale(CASE WHEN CAST(@as_of_utc AS date) - oi.due_date BETWEEN 1 AND 30 THEN oi.open_amount ELSE 0::numeric(18,4) END) AS bucket_1_30_amount,
                                 trim_scale(CASE WHEN CAST(@as_of_utc AS date) - oi.due_date BETWEEN 31 AND 60 THEN oi.open_amount ELSE 0::numeric(18,4) END) AS bucket_31_60_amount,
                                 trim_scale(CASE WHEN CAST(@as_of_utc AS date) - oi.due_date BETWEEN 61 AND 90 THEN oi.open_amount ELSE 0::numeric(18,4) END) AS bucket_61_90_amount,
                                 trim_scale(CASE WHEN CAST(@as_of_utc AS date) - oi.due_date > 90 THEN oi.open_amount ELSE 0::numeric(18,4) END) AS bucket_90_plus_amount
                             FROM open_items oi
                             JOIN doc_ab_sales_invoice si
                               ON si.document_id = oi.invoice_id
                             JOIN documents d
                               ON d.id = oi.invoice_id
                             LEFT JOIN cat_ab_client c
                               ON c.catalog_id = oi.client_id
                             LEFT JOIN cat_ab_project p
                               ON p.catalog_id = oi.project_id
                         ) x
                         """,
                baseWhereSql: "x.open_amount <> 0",
                fields:
                [
                    DateField("invoice_date", "x.invoice_date"),
                    DateField("due_date", "x.due_date"),
                    Field("client_id", "x.client_id", "uuid"),
                    Field("client_display", "x.client_display", "string"),
                    Field("project_id", "x.project_id", "uuid"),
                    Field("project_display", "x.project_display", "string"),
                    Field("invoice_id", "x.invoice_id", "uuid"),
                    Field("invoice_display", "x.invoice_display", "string"),
                    Field("aging_bucket", "x.aging_bucket", "string"),
                    Field("days_past_due", "x.days_past_due", "int32")
                ],
                measures:
                [
                    Measure("open_amount", "x.open_amount"),
                    Measure("current_amount", "x.current_amount"),
                    Measure("bucket_1_30_amount", "x.bucket_1_30_amount"),
                    Measure("bucket_31_60_amount", "x.bucket_31_60_amount"),
                    Measure("bucket_61_90_amount", "x.bucket_61_90_amount"),
                    Measure("bucket_90_plus_amount", "x.bucket_90_plus_amount")
                ]),
            new(
                datasetCode: AgencyBillingCodes.TeamUtilizationReport,
                fromSql: """
                         (
                             SELECT
                                 t.work_date AS work_date,
                                 t.document_id AS timesheet_id,
                                 COALESCE(t.display, d.number, LEFT(t.document_id::text, 8)) AS timesheet_display,
                                 t.team_member_id AS team_member_id,
                                 COALESCE(tm.display, t.team_member_id::text) AS team_member_display,
                                 t.client_id AS client_id,
                                 COALESCE(c.display, t.client_id::text) AS client_display,
                                 t.project_id AS project_id,
                                 COALESCE(p.display, t.project_id::text) AS project_display,
                                 line.service_item_id AS service_item_id,
                                 COALESCE(si.display, line.service_item_id::text) AS service_item_display,
                                 trim_scale(line.hours) AS hours_total,
                                 trim_scale(CASE WHEN line.billable THEN line.hours ELSE 0::numeric(18,4) END) AS billable_hours,
                                 trim_scale(CASE WHEN line.billable THEN 0::numeric(18,4) ELSE line.hours END) AS non_billable_hours,
                                 trim_scale(CASE WHEN line.billable THEN COALESCE(line.line_amount, 0::numeric(18,4)) ELSE 0::numeric(18,4) END) AS billable_amount,
                                 trim_scale(COALESCE(line.line_cost_amount, 0::numeric(18,4))) AS cost_amount
                             FROM doc_ab_timesheet t
                             JOIN documents d
                               ON d.id = t.document_id
                             JOIN doc_ab_timesheet__lines line
                               ON line.document_id = t.document_id
                             LEFT JOIN cat_ab_team_member tm
                               ON tm.catalog_id = t.team_member_id
                             LEFT JOIN cat_ab_client c
                               ON c.catalog_id = t.client_id
                             LEFT JOIN cat_ab_project p
                               ON p.catalog_id = t.project_id
                             LEFT JOIN cat_ab_service_item si
                               ON si.catalog_id = line.service_item_id
                             WHERE d.status = 2
                               AND t.work_date >= CAST(@from_utc AS date)
                               AND t.work_date < CAST(@to_utc_exclusive AS date)
                         ) x
                         """,
                fields:
                [
                    DateField("work_date", "x.work_date"),
                    Field("team_member_id", "x.team_member_id", "uuid"),
                    Field("team_member_display", "x.team_member_display", "string"),
                    Field("client_id", "x.client_id", "uuid"),
                    Field("client_display", "x.client_display", "string"),
                    Field("project_id", "x.project_id", "uuid"),
                    Field("project_display", "x.project_display", "string"),
                    Field("service_item_id", "x.service_item_id", "uuid"),
                    Field("service_item_display", "x.service_item_display", "string"),
                    Field("timesheet_id", "x.timesheet_id", "uuid"),
                    Field("timesheet_display", "x.timesheet_display", "string")
                ],
                measures:
                [
                    Measure("hours_total", "x.hours_total"),
                    Measure("billable_hours", "x.billable_hours"),
                    Measure("non_billable_hours", "x.non_billable_hours"),
                    Measure("billable_amount", "x.billable_amount"),
                    Measure("cost_amount", "x.cost_amount")
                ])
        ];

    private static PostgresReportFieldBinding Field(string code, string expression, string dataType)
        => new(code, expression, dataType);

    private static PostgresReportFieldBinding DateField(string code, string expression)
        => new(
            code,
            expression,
            "datetime",
            dayBucketSqlExpression: $"date_trunc('day', {expression})",
            weekBucketSqlExpression: $"date_trunc('week', {expression})",
            monthBucketSqlExpression: $"date_trunc('month', {expression})",
            quarterBucketSqlExpression: $"date_trunc('quarter', {expression})",
            yearBucketSqlExpression: $"date_trunc('year', {expression})");

    private static PostgresReportMeasureBinding Measure(string code, string expression)
        => new(code, expression, "decimal");
}
