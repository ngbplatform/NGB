using NGB.Tools.Exceptions;

namespace NGB.Accounting.Accounts;

public sealed class AccountNotFoundException : NgbNotFoundException
{
    public const string ErrorCodeConst = "coa.account.not_found";

    public AccountNotFoundException(Guid accountId)
        : base(
            message: "Account was not found.",
            errorCode: ErrorCodeConst,
            context: new Dictionary<string, object?> { ["accountId"] = accountId })
    {
        AccountId = accountId;
    }

    public AccountNotFoundException(string code)
        : base(
            message: "Account was not found.",
            errorCode: ErrorCodeConst,
            context: new Dictionary<string, object?> { ["code"] = code })
    {
        Code = code;
    }

    public Guid? AccountId { get; }

    public string? Code { get; }
}
