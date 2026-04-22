using NGB.Tools.Exceptions;

namespace NGB.Accounting.Accounts;

public enum NormalBalance
{
    Debit,
    Credit
}

public static class NormalBalanceExtensions
{
    public static NormalBalance ApplyContra(this NormalBalance value, bool isContra)
    {
        if (!isContra)
            return value;

        return value switch
        {
            NormalBalance.Debit => NormalBalance.Credit,
            NormalBalance.Credit => NormalBalance.Debit,
            _ => throw new NgbArgumentOutOfRangeException(nameof(value), value, "Unknown NormalBalance")
        };
    }
}
