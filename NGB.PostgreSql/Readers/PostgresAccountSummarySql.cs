namespace NGB.PostgreSql.Readers;

internal static class PostgresAccountSummarySql
{
    internal const string Sql = """
            WITH latest AS (
                SELECT MAX(period) AS period FROM accounting_closed_periods WHERE period <= @FromInclusive
            ), requested AS (
                SELECT * FROM unnest(@DimensionIds::uuid[], @ValueIds::uuid[]) AS p(dimension_id, value_id)
            ), matching AS (
                SELECT di.dimension_set_id FROM platform_dimension_set_items di
                JOIN requested p USING (dimension_id, value_id)
                GROUP BY di.dimension_set_id HAVING COUNT(DISTINCT di.dimension_id) = @DimensionCount
            ), amounts AS (
                SELECT b.account_id,
                    CASE WHEN l.period = @FromInclusive THEN b.opening_balance ELSE b.closing_balance END AS opening,
                    0::numeric AS debit, 0::numeric AS credit
                FROM accounting_balances b CROSS JOIN latest l
                WHERE b.period = l.period
                  AND (@DimensionCount = 0 OR b.dimension_set_id IN (SELECT dimension_set_id FROM matching))
                UNION ALL
                SELECT t.account_id,
                    CASE WHEN t.period < @FromInclusive THEN t.debit_amount - t.credit_amount ELSE 0::numeric END,
                    CASE WHEN t.period >= @FromInclusive THEN t.debit_amount ELSE 0::numeric END,
                    CASE WHEN t.period >= @FromInclusive THEN t.credit_amount ELSE 0::numeric END
                FROM accounting_turnovers t CROSS JOIN latest l
                WHERE t.period <= @ToInclusive
                  AND (l.period IS NULL OR t.period > l.period OR (l.period = @FromInclusive AND t.period = l.period))
                  AND (@DimensionCount = 0 OR t.dimension_set_id IN (SELECT dimension_set_id FROM matching))
            )
            SELECT a.account_id, a.code, a.name, a.account_type, SUM(v.opening), SUM(v.debit), SUM(v.credit), a.statement_section
            FROM amounts v JOIN accounting_accounts a ON a.account_id = v.account_id AND a.is_deleted = FALSE
            GROUP BY a.account_id, a.code, a.name, a.account_type
            """;
}
