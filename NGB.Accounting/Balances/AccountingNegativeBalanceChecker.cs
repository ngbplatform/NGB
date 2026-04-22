using NGB.Accounting.Accounts;
using NGB.Tools.Exceptions;

namespace NGB.Accounting.Balances;

public sealed class AccountingNegativeBalanceChecker(
    IChartOfAccountsProvider chartOfAccountsProvider,
    IAccountByIdResolver? accountByIdResolver = null)
{
    public async Task<IReadOnlyList<NegativeBalanceViolation>> CheckAsync(
        IEnumerable<AccountingBalance> balances,
        CancellationToken ct)
    {
        if (balances is null)
            throw new NgbArgumentRequiredException(nameof(balances));

        var chartOfAccounts = await chartOfAccountsProvider.GetAsync(ct);

        List<NegativeBalanceViolation>? result = null;
        List<AccountingBalance>? unresolvedBalances = null;
        HashSet<Guid>? unresolvedAccountIds = null;

        foreach (var balance in balances)
        {
            ct.ThrowIfCancellationRequested();

            if (balance.ClosingBalance == 0m)
                continue;

            // Most of the time the active snapshot contains the account.
            // Keep that path single-pass and allocation-free.
            if (chartOfAccounts.TryGet(balance.AccountId, out var fromSnapshot) && fromSnapshot is not null)
            {
                AppendViolationIfNeeded(ref result, balance, fromSnapshot);
                continue;
            }

            // Historic period closing may still encounter inactive accounts that are absent from the active snapshot.
            // Buffer only those rare rows for one batch resolver call instead of materializing the full input.
            if (accountByIdResolver is null)
                ThrowMissingAccount(balance);

            unresolvedBalances ??= [];
            unresolvedAccountIds ??= [];
            unresolvedBalances.Add(balance);
            unresolvedAccountIds.Add(balance.AccountId);
        }

        if (unresolvedBalances is null || unresolvedBalances.Count == 0)
            return result ?? [];

        var missingResolved = await accountByIdResolver!.GetByIdsAsync(unresolvedAccountIds!, ct);

        foreach (var balance in unresolvedBalances)
        {
            ct.ThrowIfCancellationRequested();

            if (!missingResolved.TryGetValue(balance.AccountId, out var resolved) || resolved is null)
                ThrowMissingAccount(balance);

            AppendViolationIfNeeded(ref result, balance, resolved!);
        }

        return result ?? [];
    }

    private static void AppendViolationIfNeeded(
        ref List<NegativeBalanceViolation>? result,
        AccountingBalance balance,
        Account account)
    {
        if (account.NegativeBalancePolicy == NegativeBalancePolicy.Allow)
            return;

        var isViolation = account.NormalBalance == NormalBalance.Debit
            ? balance.ClosingBalance < 0m   // credit-balance for Debit-normal account
            : balance.ClosingBalance > 0m;  // debit-balance for Credit-normal account

        if (!isViolation)
            return;

        result ??= [];
        result.Add(new NegativeBalanceViolation
        {
            Period = balance.Period,
            AccountId = account.Id,
            AccountCode = account.Code,
            AccountName = account.Name,
            AccountType = account.Type,
            Policy = account.NegativeBalancePolicy,
            DimensionSetId = balance.DimensionSetId,
            Dimensions = balance.Dimensions,
            ClosingBalance = balance.ClosingBalance
        });
    }

    private static void ThrowMissingAccount(AccountingBalance balance)
    {
        throw new NgbInvariantViolationException(
            "Account id not found while checking negative balances.",
            context: new Dictionary<string, object?>
            {
                ["accountId"] = balance.AccountId,
                ["period"] = balance.Period
            });
    }
}
