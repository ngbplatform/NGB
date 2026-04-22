using NGB.Tools.Exceptions;

namespace NGB.Runtime.Accounts.Exceptions;

public sealed class AccountHasMovementsImmutabilityViolationException(
    Guid accountId,
    IReadOnlyList<string> attemptedChanges)
    : NgbConflictException(
        message: "Account has movements and immutable fields cannot be changed.",
        errorCode: ErrorCodeConst,
        context: new Dictionary<string, object?>
        {
            ["accountId"] = accountId,
            ["attemptedChanges"] = attemptedChanges
        })
{
    public const string ErrorCodeConst = "coa.account.has_movements.immutability_violation";

    public Guid AccountId { get; } = accountId;

    public IReadOnlyList<string> AttemptedChanges { get; } = attemptedChanges;
}
