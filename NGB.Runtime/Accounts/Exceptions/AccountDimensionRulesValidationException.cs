using NGB.Tools.Exceptions;

namespace NGB.Runtime.Accounts.Exceptions;

public sealed class AccountDimensionRulesValidationException(Guid accountId, int index, string reason)
    : NgbValidationException(
        message: "Account dimension rules are invalid.",
        errorCode: ErrorCodeConst,
        context: new Dictionary<string, object?>
        {
            ["accountId"] = accountId,
            ["index"] = index,
            ["reason"] = reason
        })
{
    public const string ErrorCodeConst = "coa.account.dimension_rules.validation";

    public Guid AccountId { get; } = accountId;

    public int Index { get; } = index;

    public string Reason { get; } = reason;
}
