using NGB.PostgreSql.Reporting;

namespace NGB.PropertyManagement.PostgreSql.Reporting;

public sealed class PmAccountingLedgerAnalysisPostgresDatasetSource : IPostgresReportDatasetSource
{
    public IReadOnlyList<PostgresReportDatasetBinding> GetDatasets()
        =>
        [
            new PostgresReportDatasetBinding(
                datasetCode: "pm.accounting.ledger.analysis",
                fromSql: """
                         (
                             WITH pm_dimensions AS (
                                 SELECT
                                     (SELECT dimension_id FROM platform_dimensions WHERE code_norm = 'pm.property' LIMIT 1) AS property_dimension_id,
                                     (SELECT dimension_id FROM platform_dimensions WHERE code_norm = 'pm.party' LIMIT 1) AS party_dimension_id,
                                     (SELECT dimension_id FROM platform_dimensions WHERE code_norm = 'pm.lease' LIMIT 1) AS lease_dimension_id
                             )
                             SELECT
                                 r.entry_id,
                                 r.posting_side,
                                 r.document_id,
                                 COALESCE(d.number, LEFT(r.document_id::text, 8)) AS document_display,
                                 r.period,
                                 a.account_id,
                                 a.code AS account_code,
                                 a.name AS account_name,
                                 a.code || ' — ' || a.name AS account_display,
                                 r.dimension_set_id,
                                 prop.value_id AS property_id,
                                 party.value_id AS party_id,
                                 lease.value_id AS lease_id,
                                 r.debit_amount,
                                 r.credit_amount,
                                 (r.debit_amount - r.credit_amount) AS net_amount
                             FROM (
                                 SELECT
                                     entry_id,
                                     document_id,
                                     period,
                                     'debit'::text AS posting_side,
                                     debit_account_id AS account_id,
                                     debit_dimension_set_id AS dimension_set_id,
                                     amount AS debit_amount,
                                     0::numeric(18,4) AS credit_amount
                                 FROM accounting_register_main

                                 UNION ALL

                                 SELECT
                                     entry_id,
                                     document_id,
                                     period,
                                     'credit'::text AS posting_side,
                                     credit_account_id AS account_id,
                                     credit_dimension_set_id AS dimension_set_id,
                                     0::numeric(18,4) AS debit_amount,
                                     amount AS credit_amount
                                 FROM accounting_register_main
                             ) r
                             JOIN accounting_accounts a
                               ON a.account_id = r.account_id
                              AND a.is_deleted = FALSE
                             LEFT JOIN documents d
                               ON d.id = r.document_id
                             CROSS JOIN pm_dimensions dims
                             LEFT JOIN platform_dimension_set_items prop
                               ON prop.dimension_set_id = r.dimension_set_id
                              AND prop.dimension_id = dims.property_dimension_id
                             LEFT JOIN platform_dimension_set_items party
                               ON party.dimension_set_id = r.dimension_set_id
                              AND party.dimension_id = dims.party_dimension_id
                             LEFT JOIN platform_dimension_set_items lease
                               ON lease.dimension_set_id = r.dimension_set_id
                              AND lease.dimension_id = dims.lease_dimension_id
                         ) x
                         """,
                baseWhereSql: "x.period >= @from_utc AND x.period < @to_utc_exclusive",
                fields:
                [
                    new PostgresReportFieldBinding("entry_id", "x.entry_id", "int64"),
                    new PostgresReportFieldBinding("posting_side", "x.posting_side", "string"),
                    new PostgresReportFieldBinding("document_id", "x.document_id", "uuid"),
                    new PostgresReportFieldBinding("document_display", "x.document_display", "string"),
                    new PostgresReportFieldBinding(
                        "period_utc",
                        "x.period",
                        "datetime",
                        dayBucketSqlExpression: "date_trunc('day', x.period)",
                        weekBucketSqlExpression: "date_trunc('week', x.period)",
                        monthBucketSqlExpression: "date_trunc('month', x.period)",
                        quarterBucketSqlExpression: "date_trunc('quarter', x.period)",
                        yearBucketSqlExpression: "date_trunc('year', x.period)"),
                    new PostgresReportFieldBinding("account_id", "x.account_id", "uuid"),
                    new PostgresReportFieldBinding("account_code", "x.account_code", "string"),
                    new PostgresReportFieldBinding("account_name", "x.account_name", "string"),
                    new PostgresReportFieldBinding("account_display", "x.account_display", "string"),
                    new PostgresReportFieldBinding("dimension_set_id", "x.dimension_set_id", "uuid"),
                    new PostgresReportFieldBinding("property_id", "x.property_id", "uuid"),
                    new PostgresReportFieldBinding("party_id", "x.party_id", "uuid"),
                    new PostgresReportFieldBinding("lease_id", "x.lease_id", "uuid")
                ],
                measures:
                [
                    new PostgresReportMeasureBinding("debit_amount", "x.debit_amount", "decimal"),
                    new PostgresReportMeasureBinding("credit_amount", "x.credit_amount", "decimal"),
                    new PostgresReportMeasureBinding("net_amount", "x.net_amount", "decimal")
                ])
        ];
}
