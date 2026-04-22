using NGB.Tools.Exceptions;

namespace NGB.Accounting.Accounts;

/// <summary>
/// Chart of Accounts is an in-memory snapshot optimized for posting and reporting.
///
/// Uniqueness rules:
/// - Account.Id is the technical primary key.
/// - Account.Code uniqueness is enforced by normalized code (see <see cref="AccountCode.Normalize"/>).
/// </summary>
public sealed class ChartOfAccounts
{
    private readonly Dictionary<Guid, Account> _byId = new();

    // code_norm -> account_id (normalized string; comparer is ordinal)
    private readonly Dictionary<string, Guid> _idByCodeNorm = new(StringComparer.Ordinal);

    public IReadOnlyCollection<Account> Accounts => _byId.Values;

    // Backward-compatible alias used by older Runtime/tests.
    public IReadOnlyCollection<Account> All => Accounts;

    public void Add(Account account)
    {
        if (account is null)
            throw new NgbArgumentRequiredException(nameof(account));

        if (!_byId.TryAdd(account.Id, account))
        {
            // Duplicate technical PK.
            var existing = _byId[account.Id];
            var existingCodeNorm = AccountCode.Normalize(existing.Code);

            throw new AccountAlreadyExistsException(
                accountId: existing.Id,
                code: existing.Code,
                codeNorm: existingCodeNorm,
                existingName: existing.Name,
                attemptedAccountId: account.Id);
        }

        var codeNorm = AccountCode.Normalize(account.Code);

        if (!_idByCodeNorm.TryAdd(codeNorm, account.Id))
        {
            // roll back id insert for consistency
            _byId.Remove(account.Id);

            var existingId = _idByCodeNorm[codeNorm];
            var existing = _byId[existingId];

            throw new AccountAlreadyExistsException(
                accountId: existing.Id,
                code: account.Code,
                codeNorm: codeNorm,
                existingName: existing.Name,
                attemptedAccountId: account.Id);
        }
    }

    public Account Get(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new NgbArgumentRequiredException(nameof(code));

        var codeNorm = AccountCode.Normalize(code);

        if (_idByCodeNorm.TryGetValue(codeNorm, out var id) && _byId.TryGetValue(id, out var account))
            return account;

        throw new AccountNotFoundException(code);
    }

    public Account Get(Guid accountId)
    {
        if (_byId.TryGetValue(accountId, out var account))
            return account;

        throw new AccountNotFoundException(accountId);
    }

    public bool TryGet(string code, out Account? account)
    {
        account = null;

        if (string.IsNullOrWhiteSpace(code))
            return false;

        var codeNorm = AccountCode.Normalize(code);

        return _idByCodeNorm.TryGetValue(codeNorm, out var id) && _byId.TryGetValue(id, out account);
    }

    public bool TryGetByCode(string code, out Account? account)
        => TryGet(code, out account);

    public bool TryGet(Guid accountId, out Account? account)
        => _byId.TryGetValue(accountId, out account);
}
