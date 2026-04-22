using Dapper;
using NGB.Accounting.Accounts;
using NGB.Persistence.Readers.Accounts;
using NGB.Persistence.UnitOfWork;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.Readers;

public sealed class PostgresAccountLookupReader(IUnitOfWork uow) : IAccountLookupReader
{
    public async Task<IReadOnlyList<AccountLookupRecord>> GetByIdsAsync(
        IReadOnlyCollection<Guid> accountIds,
        CancellationToken ct = default)
    {
        if (accountIds is null)
            throw new NgbArgumentRequiredException(nameof(accountIds));

        if (accountIds.Count == 0)
            return [];

        var uniq = accountIds
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToArray();

        if (uniq.Length == 0)
            return [];

        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
                           SELECT
                               account_id AS "AccountId",
                               code       AS "Code",
                               name       AS "Name"
                           FROM accounting_accounts
                           WHERE account_id = ANY(@AccountIds)
                           ORDER BY code;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new { AccountIds = uniq },
            transaction: uow.Transaction,
            cancellationToken: ct);

        var rows = await uow.Connection.QueryAsync<AccountLookupRecord>(cmd);
        return rows.AsList();
    }
}
