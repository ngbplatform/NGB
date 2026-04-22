using Dapper;
using NGB.Accounting.Accounts;
using NGB.Persistence.Readers.Accounts;
using NGB.Persistence.UnitOfWork;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.Readers;

public sealed class PostgresRetainedEarningsAccountLookupReader(IUnitOfWork uow) : IRetainedEarningsAccountLookupReader
{
    public async Task<IReadOnlyList<AccountLookupRecord>> SearchAsync(
        string? query,
        int limit,
        CancellationToken ct = default)
    {
        if (limit is <= 0 or > 100)
            throw new NgbArgumentOutOfRangeException(nameof(limit), limit, "Limit must be between 1 and 100.");

        await uow.EnsureConnectionOpenAsync(ct);

        var trimmed = string.IsNullOrWhiteSpace(query) ? null : query.Trim();
        var pattern = trimmed is null ? null : $"%{trimmed}%";

        const string sql = """
                           SELECT
                               account_id AS "AccountId",
                               code       AS "Code",
                               name       AS "Name"
                           FROM accounting_accounts
                           WHERE is_deleted = FALSE
                             AND is_active = TRUE
                             AND statement_section = @StatementSection
                             AND is_contra = FALSE
                             AND NOT EXISTS (
                                 SELECT 1
                                 FROM accounting_account_dimension_rules r
                                 WHERE r.account_id = accounting_accounts.account_id
                                   AND r.is_required = TRUE
                             )
                             AND (
                                 @Pattern IS NULL
                                 OR code ILIKE @Pattern
                                 OR name ILIKE @Pattern
                             )
                           ORDER BY code
                           LIMIT @Limit;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new
            {
                StatementSection = (short)StatementSection.Equity,
                Pattern = pattern,
                Limit = limit
            },
            transaction: uow.Transaction,
            cancellationToken: ct);

        var rows = await uow.Connection.QueryAsync<AccountLookupRecord>(cmd);
        return rows.AsList();
    }
}
