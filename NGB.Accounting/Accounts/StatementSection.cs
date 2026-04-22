namespace NGB.Accounting.Accounts;

/// <summary>
/// Financial statement classification for an account.
///
/// This is reporting/UI metadata used to build Balance Sheet and Income Statement (P&amp;L).
/// Posting math remains purely double-entry on leaf accounts (account_id).
/// </summary>
public enum StatementSection : short
{
    Assets = 1,
    Liabilities = 2,
    Equity = 3,

    Income = 4,
    CostOfGoodsSold = 5,
    Expenses = 6,

    OtherIncome = 7,
    OtherExpense = 8
}
