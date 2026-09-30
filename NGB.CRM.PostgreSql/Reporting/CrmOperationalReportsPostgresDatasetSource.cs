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
                    SELECT
                        o.opportunity_id,
                        o.opportunity_name AS opportunity_display,
                        o.account_id AS customer_id,
                        o.account_display AS customer_display,
                        o.stage_id,
                        o.stage_display,
                        o.status,
                        o.expected_close_date,
                        o.amount,
                        o.probability,
                        o.weighted_amount
                    FROM crm_opportunities_current o
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
                    SELECT
                        o.event_at_utc,
                        o.event_type,
                        o.opportunity_id,
                        o.opportunity_name AS opportunity_display,
                        o.account_id AS customer_id,
                        o.account_display AS customer_display,
                        o.stage_id,
                        o.stage_display,
                        o.status,
                        o.amount,
                        o.probability
                    FROM crm_opportunity_history o
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
                    SELECT
                        history.source_document_id AS document_id,
                        COALESCE(history.document_display, history.source_document_id::text) AS document_display,
                        history.event_at_utc,
                        history.funnel_step,
                        history.lead_source,
                        history.industry,
                        1::bigint AS lead_count
                    FROM (
                        SELECT
                            li.document_id AS source_document_id,
                            li.display AS document_display,
                            d.posted_at_utc AS event_at_utc,
                            '01 Intake'::text AS funnel_step,
                            li.lead_source,
                            li.industry
                        FROM doc_crm_lead_intake li
                        JOIN documents d ON d.id = li.document_id AND d.status = 2

                        UNION ALL

                        SELECT
                            lq.document_id,
                            lq.display,
                            d.posted_at_utc,
                            CASE lq.qualification_state
                                WHEN 'Qualified' THEN '02 Qualified'
                                WHEN 'Disqualified' THEN '02 Disqualified'
                                WHEN 'Converted' THEN '03 Converted'
                                ELSE '02 ' || lq.qualification_state
                            END,
                            li.lead_source,
                            li.industry
                        FROM doc_crm_lead_qualification lq
                        JOIN documents d ON d.id = lq.document_id AND d.status = 2
                        JOIN doc_crm_lead_intake li ON li.document_id = lq.lead_intake_id

                        UNION ALL

                        SELECT
                            lc.document_id,
                            lc.display,
                            d.posted_at_utc,
                            '03 Converted'::text,
                            li.lead_source,
                            li.industry
                        FROM doc_crm_lead_conversion lc
                        JOIN documents d ON d.id = lc.document_id AND d.status = 2
                        JOIN doc_crm_lead_intake li ON li.document_id = lc.lead_intake_id
                    ) history
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
                    SELECT
                        activity.activity_id,
                        activity.activity_date,
                        activity.activity_type,
                        activity.subject,
                        activity.lead_intake_id,
                        activity.account_id AS customer_id,
                        a.display AS customer_display,
                        activity.contact_id,
                        c.display AS contact_display,
                        activity.opportunity_id,
                        activity.due_at_utc,
                        activity.completed_at_utc,
                        activity.outcome
                    FROM crm_activities_current activity
                    LEFT JOIN cat_crm_account a ON a.catalog_id = activity.account_id
                    LEFT JOIN cat_crm_contact c ON c.catalog_id = activity.contact_id
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
                    SELECT
                        quote.quote_id,
                        quote.quote_date,
                        quote.opportunity_id,
                        quote.account_id AS customer_id,
                        quote.account_display AS customer_display,
                        quote.contact_id,
                        quote.contact_display,
                        quote.valid_until,
                        quote.currency,
                        quote.quote_status,
                        quote.amount
                    FROM crm_quotes_current quote
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
