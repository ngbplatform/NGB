using NGB.PostgreSql.Reporting;

namespace NGB.CRM.PostgreSql.Reporting;

public sealed class CrmOperationalReportsPostgresDatasetSource : IPostgresReportDatasetSource
{
    public IReadOnlyList<PostgresReportDatasetBinding> GetDatasets()
        =>
        [
            new(
                datasetCode: CrmCodes.SalesPipelineReport,
                fromSql:
                """
                (
                    WITH latest AS (
                        SELECT DISTINCT ON (dimension_set_id) *
                        FROM refreg_crm_opportunities__records
                        ORDER BY dimension_set_id, recorded_at_utc DESC, record_id DESC
                    )
                    SELECT
                        o.opportunity_id,
                        o.opportunity_name AS opportunity_display,
                        o.account_id AS customer_id,
                        a.display AS customer_display,
                        o.stage_id,
                        s.display AS stage_display,
                        o.status,
                        o.expected_close_date,
                        o.amount,
                        o.probability,
                        ROUND(o.amount * o.probability / 100.0, 4) AS weighted_amount
                    FROM latest o
                    LEFT JOIN cat_crm_account a ON a.catalog_id = o.account_id
                    LEFT JOIN cat_crm_opportunity_stage s ON s.catalog_id = o.stage_id
                    WHERE NOT o.is_deleted
                ) x
                """,
                fields:
                [
                    Field("opportunity_id", "x.opportunity_id", "uuid"),
                    Field("opportunity_display", "x.opportunity_display", "string"),
                    Field("customer_id", "x.customer_id", "uuid"),
                    Field("customer_display", "x.customer_display", "string"),
                    Field("stage_id", "x.stage_id", "uuid"),
                    Field("stage_display", "x.stage_display", "string"),
                    Field("status", "x.status", "string"),
                    Field("expected_close_date", "x.expected_close_date", "date")
                ],
                measures:
                [
                    Measure("amount", "x.amount", "decimal"),
                    Measure("weighted_amount", "x.weighted_amount", "decimal"),
                    Measure("probability", "x.probability", "decimal")
                ]),
            new(
                datasetCode: CrmCodes.OpportunityHistoryReport,
                fromSql:
                """
                (
                    WITH latest AS (
                        SELECT DISTINCT ON (dimension_set_id)
                            dimension_set_id,
                            is_deleted
                        FROM refreg_crm_opportunities__records
                        ORDER BY dimension_set_id, recorded_at_utc DESC, record_id DESC
                    ),
                    live_keys AS (
                        SELECT dimension_set_id
                        FROM latest
                        WHERE NOT is_deleted
                    )
                    SELECT
                        o.event_at_utc,
                        o.event_type,
                        o.opportunity_id,
                        o.opportunity_name AS opportunity_display,
                        o.account_id AS customer_id,
                        a.display AS customer_display,
                        o.stage_id,
                        s.display AS stage_display,
                        o.status,
                        o.amount,
                        o.probability
                    FROM refreg_crm_opportunities__records o
                    JOIN live_keys lk ON lk.dimension_set_id = o.dimension_set_id
                    LEFT JOIN cat_crm_account a ON a.catalog_id = o.account_id
                    LEFT JOIN cat_crm_opportunity_stage s ON s.catalog_id = o.stage_id
                    WHERE NOT o.is_deleted
                ) x
                """,
                fields:
                [
                    Field("event_at_utc", "x.event_at_utc", "datetime"),
                    Field("event_type", "x.event_type", "string"),
                    Field("opportunity_id", "x.opportunity_id", "uuid"),
                    Field("opportunity_display", "x.opportunity_display", "string"),
                    Field("customer_id", "x.customer_id", "uuid"),
                    Field("customer_display", "x.customer_display", "string"),
                    Field("stage_id", "x.stage_id", "uuid"),
                    Field("stage_display", "x.stage_display", "string"),
                    Field("status", "x.status", "string")
                ],
                measures:
                [
                    Measure("amount", "x.amount", "decimal"),
                    Measure("probability", "x.probability", "decimal")
                ]),
            new(
                datasetCode: CrmCodes.LeadConversionFunnelReport,
                fromSql:
                """
                (
                    WITH latest AS (
                        SELECT DISTINCT ON (dimension_set_id) *
                        FROM refreg_crm_lead_funnel__records
                        ORDER BY dimension_set_id, recorded_at_utc DESC, record_id DESC
                    )
                    SELECT
                        source_document_id AS document_id,
                        source_document_id::text AS document_display,
                        event_at_utc,
                        funnel_step,
                        lead_source,
                        industry,
                        1::bigint AS lead_count
                    FROM latest
                    WHERE NOT is_deleted
                ) x
                """,
                fields:
                [
                    Field("event_at_utc", "x.event_at_utc", "datetime"),
                    Field("funnel_step", "x.funnel_step", "string"),
                    Field("lead_source", "x.lead_source", "string"),
                    Field("industry", "x.industry", "string"),
                    Field("document_id", "x.document_id", "uuid"),
                    Field("document_display", "x.document_display", "string")
                ],
                measures:
                [
                    Measure("lead_count", "x.lead_count", "int64")
                ]),
            new(
                datasetCode: CrmCodes.ActivitySummaryReport,
                fromSql:
                """
                (
                    WITH latest AS (
                        SELECT DISTINCT ON (dimension_set_id) *
                        FROM refreg_crm_activities__records
                        ORDER BY dimension_set_id, recorded_at_utc DESC, record_id DESC
                    )
                    SELECT
                        latest.activity_id,
                        latest.activity_date,
                        latest.activity_type,
                        latest.subject,
                        latest.lead_intake_id,
                        latest.account_id AS customer_id,
                        a.display AS customer_display,
                        latest.contact_id,
                        c.display AS contact_display,
                        latest.opportunity_id,
                        latest.due_at_utc,
                        latest.completed_at_utc,
                        latest.outcome
                    FROM latest
                    LEFT JOIN cat_crm_account a ON a.catalog_id = latest.account_id
                    LEFT JOIN cat_crm_contact c ON c.catalog_id = latest.contact_id
                    WHERE NOT is_deleted
                ) x
                """,
                fields:
                [
                    Field("activity_date", "x.activity_date", "date"),
                    Field("activity_type", "x.activity_type", "string"),
                    Field("customer_id", "x.customer_id", "uuid"),
                    Field("customer_display", "x.customer_display", "string"),
                    Field("contact_id", "x.contact_id", "uuid"),
                    Field("contact_display", "x.contact_display", "string"),
                    Field("outcome", "x.outcome", "string")
                ],
                measures:
                [
                    Measure("activity_count", "1", "int64")
                ]),
            new(
                datasetCode: CrmCodes.QuoteRegisterReport,
                fromSql:
                """
                (
                    WITH latest AS (
                        SELECT DISTINCT ON (dimension_set_id) *
                        FROM refreg_crm_quotes__records
                        ORDER BY dimension_set_id, recorded_at_utc DESC, record_id DESC
                    )
                    SELECT
                        latest.quote_id,
                        latest.quote_date,
                        latest.opportunity_id,
                        latest.account_id AS customer_id,
                        a.display AS customer_display,
                        latest.contact_id,
                        c.display AS contact_display,
                        latest.valid_until,
                        latest.currency,
                        latest.quote_status,
                        latest.amount
                    FROM latest
                    LEFT JOIN cat_crm_account a ON a.catalog_id = latest.account_id
                    LEFT JOIN cat_crm_contact c ON c.catalog_id = latest.contact_id
                    WHERE NOT is_deleted
                ) x
                """,
                fields:
                [
                    Field("quote_date", "x.quote_date", "date"),
                    Field("quote_status", "x.quote_status", "string"),
                    Field("customer_id", "x.customer_id", "uuid"),
                    Field("customer_display", "x.customer_display", "string"),
                    Field("contact_id", "x.contact_id", "uuid"),
                    Field("contact_display", "x.contact_display", "string"),
                    Field("currency", "x.currency", "string")
                ],
                measures:
                [
                    Measure("amount", "x.amount", "decimal"),
                    Measure("quote_count", "1", "int64")
                ])
        ];

    private static PostgresReportFieldBinding Field(string code, string expression, string dataType)
        => new(code, expression, dataType);

    private static PostgresReportMeasureBinding Measure(string code, string expression, string dataType)
        => new(code, expression, dataType);
}
