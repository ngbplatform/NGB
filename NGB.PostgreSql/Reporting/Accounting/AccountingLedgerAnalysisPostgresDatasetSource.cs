namespace NGB.PostgreSql.Reporting.Accounting;

public sealed class AccountingLedgerAnalysisPostgresDatasetSource : IPostgresReportDatasetSource
{
    public IReadOnlyList<PostgresReportDatasetBinding> GetDatasets()
        =>
        [
            new PostgresReportDatasetBinding(
                datasetCode: "accounting.ledger.analysis",
                fromSql: """
                         (
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
                    new PostgresReportFieldBinding("dimension_set_id", "x.dimension_set_id", "uuid")
                ],
                measures:
                [
                    new PostgresReportMeasureBinding("debit_amount", "x.debit_amount", "decimal"),
                    new PostgresReportMeasureBinding("credit_amount", "x.credit_amount", "decimal"),
                    new PostgresReportMeasureBinding("net_amount", "x.net_amount", "decimal")
                ])
        ];
}
