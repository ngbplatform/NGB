using NGB.Tools.Exceptions;

namespace NGB.Runtime.Accounts.Exceptions;

public sealed class AccountDeletedException(Guid accountId) : NgbConflictException(
    message: "Account is deleted and cannot be modified.",
    errorCode: ErrorCodeConst,
    context: new Dictionary<string, object?> { ["accountId"] = accountId })
{
    public const string ErrorCodeConst = "coa.account.deleted";

    public Guid AccountId { get; } = accountId;
}
