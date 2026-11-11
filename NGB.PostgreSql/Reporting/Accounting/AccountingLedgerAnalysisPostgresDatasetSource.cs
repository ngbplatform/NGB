using NGB.Core.Reporting;
using NGB.Contracts.Reporting;

namespace NGB.PostgreSql.Reporting.Accounting;

public sealed class AccountingLedgerAnalysisPostgresDatasetSource : IPostgresReportDatasetSource
{
    public IReadOnlyList<PostgresReportDatasetBinding> GetDatasets()
        =>
        [
            new PostgresReportDatasetBinding(
                datasetCode: AccountingReportCodes.LedgerAnalysis,
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
                ],
                cursorKeyFieldCodes: ["entry_id", "posting_side"],
                aggregateSource: SelectAccountAggregateSource)
        ];

    public static PostgresReportSqlSource? SelectAccountAggregateSource(PostgresReportExecutionRequest request)
    {
        if (SupportsOrderedDetails(request))
            return OrderedDetailSource;

        // Sum-only account/period shapes can aggregate before joining account labels.
        // Keep exact timestamps so time grains, predicates and selections retain their semantics.
        // Details, dimensions and non-additive measures still use raw observations.
        static bool AggregateField(string field)
            => field is "account_id" or "account_code" or "account_name" or "account_display" or "period_utc";

        if (request.DetailFields.Count != 0 || request.Measures.Count == 0
            || request.Measures.Any(m => m.Aggregation != ReportAggregationKind.Sum)
            || request.RowGroups.Concat(request.ColumnGroups).Any(g => !AggregateField(g.FieldCode))
            || request.Predicates.Any(p => !AggregateField(p.FieldCode))
            || request.Sorts.Any(s => s.MeasureCode is null && !AggregateField(s.FieldCode))
            || request.Selection?.Fields.Any(f => !AggregateField(f.FieldCode)) == true)
        {
            return null;
        }

        if (request.RowGroups.Concat(request.ColumnGroups).Any(g => g.FieldCode == "period_utc")
            || request.Predicates.Any(p => p.FieldCode == "period_utc")
            || request.Sorts.Any(s => s.MeasureCode is null && s.FieldCode == "period_utc")
            || request.Selection?.Fields.Any(f => f.FieldCode == "period_utc") == true)
        {
            return AccountPeriodAggregateSource;
        }

        return new PostgresReportSqlSource("""
            (
                SELECT a.account_id, a.code AS account_code, a.name AS account_name,
                       a.code || ' — ' || a.name AS account_display,
                       SUM(r.debit_amount) AS debit_amount, SUM(r.credit_amount) AS credit_amount,
                       SUM(r.debit_amount - r.credit_amount) AS net_amount
                FROM (
                    SELECT side.account_id, side.debit_amount, side.credit_amount
                    FROM (
                        SELECT debit_account_id, credit_account_id, SUM(amount) AS amount
                        FROM accounting_register_main
                        WHERE period >= @from_utc AND period < @to_utc_exclusive
                          AND period_month >= date_trunc('month', @from_utc AT TIME ZONE 'UTC')::date
                          AND period_month <= date_trunc('month', (@to_utc_exclusive - INTERVAL '1 microsecond') AT TIME ZONE 'UTC')::date
                        GROUP BY debit_account_id, credit_account_id
                    ) entries
                    CROSS JOIN LATERAL (VALUES
                        (entries.debit_account_id, entries.amount, 0::numeric),
                        (entries.credit_account_id, 0::numeric, entries.amount)
                    ) side(account_id, debit_amount, credit_amount)
                ) r
                JOIN accounting_accounts a ON a.account_id = r.account_id AND a.is_deleted = FALSE
                GROUP BY a.account_id, a.code, a.name
            ) x
            """);
    }

    private static bool SupportsOrderedDetails(PostgresReportExecutionRequest request)
    {
        if (request.Paging.DisablePaging
            || request.RowGroups.Count != 0
            || request.ColumnGroups.Count != 0
            || request.Predicates.Count != 0
            || request.Selection is not null
            || request.DistinctGroups
            || request.DetailFields.Count != 3
            || request.Measures.Count == 0
            || request.Measures.Any(m => m.Aggregation != ReportAggregationKind.Sum
                || m.MeasureCode is not ("debit_amount" or "credit_amount" or "net_amount")))
        {
            return false;
        }

        var fields = request.DetailFields
            .Select(f => f.FieldCode)
            .ToHashSet(StringComparer.Ordinal);

        if (!fields.SetEquals(["period_utc", "account_display", "document_display"]))
            return false;

        if (request.Sorts.Count == 0)
            return request.DetailFields[0].FieldCode == "period_utc";

        return request.Sorts[0] is
               {
                   FieldCode: "period_utc",
                   MeasureCode: null,
                   Direction: ReportSortDirection.Asc,
                   TimeGrain: null
               }
            && request.Sorts.All(s => s.MeasureCode is null
                && s.TimeGrain is null
                && !s.AppliesToColumnAxis
                && fields.Contains(s.FieldCode));
    }

    // Preserve the period prefix so PostgreSQL can incrementally sort a page instead of
    // sorting the entire range. The outer builder still groups equal labels and applies
    // the original cursor/order; a raw-row LIMIT here would change accounting results.
    private static readonly PostgresReportSqlSource OrderedDetailSource = new("""
        (
            SELECT entries.period, entries.document_id, side.account_id,
                   a.code || ' — ' || a.name AS account_display,
                   COALESCE(d.number, LEFT(entries.document_id::text, 8)) AS document_display,
                   side.debit_amount, side.credit_amount,
                   side.debit_amount - side.credit_amount AS net_amount
            FROM (
                SELECT period, entry_id, document_id, debit_account_id, credit_account_id, amount
                FROM accounting_register_main
                WHERE period >= @from_utc AND period < @to_utc_exclusive
                ORDER BY period, entry_id
                OFFSET 0
            ) entries
            CROSS JOIN LATERAL (VALUES
                (entries.debit_account_id, entries.amount, 0::numeric(18,4)),
                (entries.credit_account_id, 0::numeric(18,4), entries.amount)
            ) side(account_id, debit_amount, credit_amount)
            JOIN accounting_accounts a ON a.account_id = side.account_id AND a.is_deleted = FALSE
            LEFT JOIN documents d ON d.id = entries.document_id
        ) x
        """);

    private static readonly PostgresReportSqlSource AccountPeriodAggregateSource = new("""
        (
            SELECT a.account_id, a.code AS account_code, a.name AS account_name,
                   a.code || ' — ' || a.name AS account_display, r.period,
                   SUM(r.debit_amount) AS debit_amount, SUM(r.credit_amount) AS credit_amount,
                   SUM(r.debit_amount - r.credit_amount) AS net_amount
            FROM (
                SELECT side.account_id, entries.period, side.debit_amount, side.credit_amount
                FROM (
                    SELECT debit_account_id, credit_account_id, period, SUM(amount) AS amount
                    FROM accounting_register_main
                    WHERE period >= @from_utc AND period < @to_utc_exclusive
                      AND period_month >= date_trunc('month', @from_utc AT TIME ZONE 'UTC')::date
                      AND period_month <= date_trunc('month', (@to_utc_exclusive - INTERVAL '1 microsecond') AT TIME ZONE 'UTC')::date
                    GROUP BY debit_account_id, credit_account_id, period
                ) entries
                CROSS JOIN LATERAL (VALUES
                    (entries.debit_account_id, entries.amount, 0::numeric),
                    (entries.credit_account_id, 0::numeric, entries.amount)
                ) side(account_id, debit_amount, credit_amount)
            ) r
            JOIN accounting_accounts a ON a.account_id = r.account_id AND a.is_deleted = FALSE
            GROUP BY a.account_id, a.code, a.name, r.period
        ) x
        """);
}
