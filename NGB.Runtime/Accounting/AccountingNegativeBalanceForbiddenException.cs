using NGB.Tools.Exceptions;

namespace NGB.Runtime.Accounting;

/// <summary>
/// Thrown when an operation would result in a forbidden negative balance.
/// </summary>
public sealed class AccountingNegativeBalanceForbiddenException(
    string message,
    IReadOnlyDictionary<string, object?>? context = null)
    : NgbForbiddenException(
        message: message,
        errorCode: ErrorCodeConst,
        context: context)
{
    public const string ErrorCodeConst = "accounting.negative_balance.forbidden";
}
