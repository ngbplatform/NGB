using NGB.Tools.Exceptions;

namespace NGB.Accounting.Accounts;

/// <summary>
/// Default normal-balance conventions for financial statements.
/// </summary>
public static class NormalBalanceDefaults
{
    public static NormalBalance FromStatementSection(StatementSection section) => section switch
    {
        // Balance Sheet
        StatementSection.Assets => NormalBalance.Debit,
        StatementSection.Liabilities => NormalBalance.Credit,
        StatementSection.Equity => NormalBalance.Credit,

        // Income Statement (P&L)
        StatementSection.Income => NormalBalance.Credit,
        StatementSection.CostOfGoodsSold => NormalBalance.Debit,
        StatementSection.Expenses => NormalBalance.Debit,
        StatementSection.OtherIncome => NormalBalance.Credit,
        StatementSection.OtherExpense => NormalBalance.Debit,

        _ => throw new NgbArgumentOutOfRangeException(nameof(section), section, "Unknown StatementSection")
    };
}
