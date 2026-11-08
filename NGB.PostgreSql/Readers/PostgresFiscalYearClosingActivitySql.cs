namespace NGB.PostgreSql.Readers;

internal static class PostgresFiscalYearClosingActivitySql
{
    // Remove only the stored CloseFiscalYear effect from P&L activity. The ledger,
    // ordinary turnovers and month-end balances remain authoritative and unchanged.
    internal const string Corrections = """
        SELECT e.period_month AS period, side.account_id, side.dimension_set_id,
               side.debit_amount, side.credit_amount
        FROM accounting_posting_state s
        JOIN accounting_register_main e ON e.document_id = s.document_id
        CROSS JOIN LATERAL (
            VALUES (e.debit_account_id, e.debit_dimension_set_id, -e.amount, 0::numeric),
                   (e.credit_account_id, e.credit_dimension_set_id, 0::numeric, -e.amount)
        ) AS side(account_id, dimension_set_id, debit_amount, credit_amount)
        WHERE s.operation = 4 -- PostingOperation.CloseFiscalYear
          AND e.period_month >= @FromInclusive::date
          AND e.period_month <= @ToInclusive::date
        """;
}
