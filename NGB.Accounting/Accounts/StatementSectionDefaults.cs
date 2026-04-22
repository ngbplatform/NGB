using NGB.Tools.Exceptions;

namespace NGB.Accounting.Accounts;

/// <summary>
/// Default mapping between <see cref="AccountType"/> and <see cref="StatementSection"/>.
///
/// IMPORTANT:
/// - This is a pragmatic default to keep account creation simple.
/// - Administration UI may allow overriding StatementSection for special cases
///   (e.g. contra accounts, other income/expense, etc.).
/// </summary>
public static class StatementSectionDefaults
{
    public static StatementSection FromAccountType(AccountType type) => type switch
    {
        AccountType.Asset => StatementSection.Assets,
        AccountType.Liability => StatementSection.Liabilities,
        AccountType.Equity => StatementSection.Equity,
        AccountType.Income => StatementSection.Income,
        AccountType.Expense => StatementSection.Expenses,
        _ => throw new NgbArgumentOutOfRangeException(nameof(type), type, "Unknown account type")
    };
}
