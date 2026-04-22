using NGB.Accounting.Accounts;
using NGB.Persistence.Accounts;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Accounts;

/// <summary>
/// Runtime implementation that resolves accounts by id from persistence.
/// Used as a fallback when an account is not present in the active CoA snapshot
/// (e.g. inactive accounts with historic movements during period closing/rebuild).
/// </summary>
public sealed class AccountByIdResolver(IChartOfAccountsRepository repo) : IAccountByIdResolver
{
    public async Task<Account?> GetByIdAsync(Guid accountId, CancellationToken ct = default)
    {
        var admin = await repo.GetAdminByIdAsync(accountId, ct);
        if (admin is null)
            return null;

        return admin.IsDeleted ? null : admin.Account;
    }

    public async Task<IReadOnlyDictionary<Guid, Account>> GetByIdsAsync(
        IReadOnlyCollection<Guid> accountIds,
        CancellationToken ct = default)
    {
        if (accountIds is null)
            throw new NgbArgumentRequiredException(nameof(accountIds));

        if (accountIds.Count == 0)
            return new Dictionary<Guid, Account>();

        var admins = await repo.GetAdminByIdsAsync(accountIds, ct);
        var map = new Dictionary<Guid, Account>(capacity: admins.Count);
        
        foreach (var a in admins)
        {
            if (a.IsDeleted)
                continue;

            // If duplicates were passed, last wins - same account anyway.
            map[a.Account.Id] = a.Account;
        }

        return map;
    }
}
