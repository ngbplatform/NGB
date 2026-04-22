using NGB.Tools.Exceptions;

namespace NGB.Accounting.Accounts;

public sealed class AccountAlreadyExistsException(
    Guid accountId,
    string code,
    string codeNorm,
    string existingName,
    Guid? attemptedAccountId = null)
    : NgbConflictException(
        message: "Account already exists.",
        errorCode: ErrorCodeConst,
        context: BuildContext(accountId, code, codeNorm, existingName, attemptedAccountId))
{
    public const string ErrorCodeConst = "coa.account.already_exists";

    public Guid? AccountId { get; } = accountId;

    public string? Code { get; } = code;

    public string? CodeNorm { get; } = codeNorm;

    public string? ExistingName { get; } = existingName;

    public Guid? AttemptedAccountId { get; } = attemptedAccountId;

    private static IReadOnlyDictionary<string, object?> BuildContext(
        Guid accountId,
        string code,
        string codeNorm,
        string existingName,
        Guid? attemptedAccountId)
    {
        var ctx = new Dictionary<string, object?>
        {
            ["accountId"] = accountId,
            ["code"] = code,
            ["codeNorm"] = codeNorm,
            ["existingName"] = existingName,
            ["attemptedAccountId"] = attemptedAccountId
        };

        return ctx;
    }
}
