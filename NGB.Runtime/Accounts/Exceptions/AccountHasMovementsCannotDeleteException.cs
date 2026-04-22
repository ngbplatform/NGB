using NGB.Tools.Exceptions;

namespace NGB.Runtime.Accounts.Exceptions;

public sealed class AccountHasMovementsCannotDeleteException(Guid accountId) : NgbConflictException(
    message: "Account has movements and cannot be deleted.",
    errorCode: ErrorCodeConst,
    context: new Dictionary<string, object?> { ["accountId"] = accountId })
{
    public const string ErrorCodeConst = "coa.account.has_movements.cannot_delete";

    public Guid AccountId { get; } = accountId;
}
