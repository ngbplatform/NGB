using Dapper;
using NGB.Accounting.Accounts;
using NGB.Accounting.CashFlow;
using NGB.Accounting.Dimensions;
using NGB.Persistence.Accounts;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.UnitOfWork;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.Accounts;

public sealed class PostgresChartOfAccountsRepository(IUnitOfWork uow) : IChartOfAccountsRepository
{
    public async Task<IReadOnlyList<Account>> GetAllAsync(CancellationToken ct)
    {
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
                           SELECT
                               account_id             AS "AccountId",
                               code                   AS "Code",
                               name                   AS "Name",
                               account_type           AS "AccountType",
                               statement_section       AS "StatementSection",
                               cash_flow_role          AS "CashFlowRole",
                               cash_flow_line_code     AS "CashFlowLineCode",
                               is_contra              AS "IsContra",
                               negative_balance_policy AS "NegativeBalancePolicy",
                               is_active              AS "IsActive",
                               is_deleted             AS "IsDeleted"
                           FROM accounting_accounts
                           WHERE is_deleted = FALSE AND is_active = TRUE;
                           """;

        var cmd = new CommandDefinition(sql, transaction: uow.Transaction, cancellationToken: ct);
        var rows = (await uow.Connection.QueryAsync<AccountRow>(cmd)).ToList();

        var ruleMap = await LoadDimensionRulesAsync(rows.Select(x => x.AccountId).ToArray(), ct);

        var list = new List<Account>();
        foreach (var r in rows)
        {
            // NOTE: The in-memory domain model currently does not expose IsActive/IsDeleted.
            // Repository filters out inactive/deleted accounts for runtime/posting snapshots.

            ruleMap.TryGetValue(r.AccountId, out var rules);
            list.Add(ToAccount(r, rules));
        }

        return list;
    }

    public async Task<IReadOnlyList<ChartOfAccountsAdminItem>> GetForAdminAsync(bool includeDeleted, CancellationToken ct)
    {
        await uow.EnsureConnectionOpenAsync(ct);

        // For administration screens we usually need inactive accounts (archived), and optionally deleted ones.
        // Soft-deleted accounts are still kept for audit/history; UI can decide how to render them.

        var sql = includeDeleted
            ? """
               SELECT
                   account_id             AS "AccountId",
                   code                   AS "Code",
                   name                   AS "Name",
                   account_type           AS "AccountType",
                   statement_section       AS "StatementSection",
                   cash_flow_role          AS "CashFlowRole",
                   cash_flow_line_code     AS "CashFlowLineCode",
                   is_contra              AS "IsContra",
                   negative_balance_policy AS "NegativeBalancePolicy",
                   is_active              AS "IsActive",
                   is_deleted             AS "IsDeleted"
               FROM accounting_accounts
               ORDER BY code;
               """
            : """
               SELECT
                   account_id             AS "AccountId",
                   code                   AS "Code",
                   name                   AS "Name",
                   account_type           AS "AccountType",
                   statement_section       AS "StatementSection",
                   cash_flow_role          AS "CashFlowRole",
                   cash_flow_line_code     AS "CashFlowLineCode",
                   is_contra              AS "IsContra",
                   negative_balance_policy AS "NegativeBalancePolicy",
                   is_active              AS "IsActive",
                   is_deleted             AS "IsDeleted"
               FROM accounting_accounts
               WHERE is_deleted = FALSE
               ORDER BY code;
               """;

        var cmd = new CommandDefinition(sql, transaction: uow.Transaction, cancellationToken: ct);
        var rows = (await uow.Connection.QueryAsync<AccountRow>(cmd)).ToList();

        var ruleMap = await LoadDimensionRulesAsync(rows.Select(x => x.AccountId).ToArray(), ct);

        var list = new List<ChartOfAccountsAdminItem>();
        foreach (var r in rows)
        {
            ruleMap.TryGetValue(r.AccountId, out var rules);
            var acc = ToAccount(r, rules);
            list.Add(new ChartOfAccountsAdminItem
            {
                Account = acc,
                IsActive = r.IsActive,
                IsDeleted = r.IsDeleted,
            });
        }

        return list;
    }

    public async Task<ChartOfAccountsAdminItem?> GetAdminByIdAsync(Guid accountId, CancellationToken ct = default)
    {
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
                           SELECT
                               account_id             AS "AccountId",
                               code                   AS "Code",
                               name                   AS "Name",
                               account_type           AS "AccountType",
                               statement_section       AS "StatementSection",
                               cash_flow_role          AS "CashFlowRole",
                               cash_flow_line_code     AS "CashFlowLineCode",
                               is_contra              AS "IsContra",
                               negative_balance_policy AS "NegativeBalancePolicy",
                               is_active              AS "IsActive",
                               is_deleted             AS "IsDeleted"
                           FROM accounting_accounts
                           WHERE account_id = @AccountId
                           LIMIT 1;
                           """;

        var cmd = new CommandDefinition(sql, new { AccountId = accountId }, transaction: uow.Transaction, cancellationToken: ct);
        var row = await uow.Connection.QuerySingleOrDefaultAsync<AccountRow>(cmd);
        if (row is null)
            return null;

        var ruleMap = await LoadDimensionRulesAsync([row.AccountId], ct);
        ruleMap.TryGetValue(row.AccountId, out var rules);
        var acc = ToAccount(row, rules);
        return new ChartOfAccountsAdminItem
        {
            Account = acc,
            IsActive = row.IsActive,
            IsDeleted = row.IsDeleted,
        };
    }

    public async Task<IReadOnlyList<ChartOfAccountsAdminItem>> GetAdminByIdsAsync(
        IReadOnlyCollection<Guid> accountIds,
        CancellationToken ct = default)
    {
        if (accountIds is null)
            throw new NgbArgumentRequiredException(nameof(accountIds));

        if (accountIds.Count == 0)
            return Array.Empty<ChartOfAccountsAdminItem>();

        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
                           SELECT
                               account_id             AS "AccountId",
                               code                   AS "Code",
                               name                   AS "Name",
                               account_type           AS "AccountType",
                               statement_section       AS "StatementSection",
                               cash_flow_role          AS "CashFlowRole",
                               cash_flow_line_code     AS "CashFlowLineCode",
                               is_contra              AS "IsContra",
                               negative_balance_policy AS "NegativeBalancePolicy",
                               is_active              AS "IsActive",
                               is_deleted             AS "IsDeleted"
                           FROM accounting_accounts
                           WHERE account_id = ANY(@AccountIds)
                           ORDER BY code;
                           """;

        var cmd = new CommandDefinition(sql, new { AccountIds = accountIds.ToArray() }, transaction: uow.Transaction, cancellationToken: ct);
        var rows = (await uow.Connection.QueryAsync<AccountRow>(cmd)).ToList();

        var ruleMap = await LoadDimensionRulesAsync(rows.Select(x => x.AccountId).ToArray(), ct);

        var list = new List<ChartOfAccountsAdminItem>();
        foreach (var r in rows)
        {
            ruleMap.TryGetValue(r.AccountId, out var rules);
            var acc = ToAccount(r, rules);
            list.Add(new ChartOfAccountsAdminItem
            {
                Account = acc,
                IsActive = r.IsActive,
                IsDeleted = r.IsDeleted
            });
        }

        return list;
    }

    public async Task<string?> GetCodeByIdAsync(Guid accountId, CancellationToken ct = default)
    {
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
                           SELECT code
                           FROM accounting_accounts
                           WHERE account_id = @AccountId AND is_deleted = FALSE;
                           """;

        var cmd = new CommandDefinition(sql, new { AccountId = accountId }, transaction: uow.Transaction, cancellationToken: ct);
        return await uow.Connection.QuerySingleOrDefaultAsync<string?>(cmd);
    }

    public async Task<bool> HasMovementsAsync(Guid accountId, CancellationToken ct = default)
    {
        await uow.EnsureConnectionOpenAsync(ct);

        // Any reference in the register means the account has movements and becomes partially immutable.
        const string sql = """
                           SELECT EXISTS (
                               SELECT 1
                               FROM accounting_register_main
                               WHERE debit_account_id = @AccountId
                                  OR credit_account_id = @AccountId
                               LIMIT 1
                           );
                           """;

        var cmd = new CommandDefinition(sql, new { AccountId = accountId }, transaction: uow.Transaction, cancellationToken: ct);
        return await uow.Connection.ExecuteScalarAsync<bool>(cmd);
    }

    public async Task CreateAsync(Account account, bool isActive = true, CancellationToken ct = default)
    {
        await uow.EnsureOpenForTransactionAsync(ct);

        const string sql = """
                           INSERT INTO accounting_accounts(
                               account_id,
                               code,
                               name,
                               account_type,
                               statement_section,
                               cash_flow_role,
                               cash_flow_line_code,
                               is_contra,
                               negative_balance_policy,
                               is_active,
                               is_deleted,
                               created_at_utc,
                               updated_at_utc
                           )
                           VALUES (
                               @AccountId,
                               @Code,
                               @Name,
                               @AccountType,
                               @StatementSection,
                               @CashFlowRole,
                               @CashFlowLineCode,
                               @IsContra,
                               @NegativeBalancePolicy,
                               @IsActive,
                               FALSE,
                               NOW(),
                               NOW()
                           );
                           """;

        var p = new
        {
            AccountId = account.Id,
            account.Code,
            account.Name,
            AccountType = (short)account.Type,
            StatementSection = (short)account.StatementSection,
            CashFlowRole = (short)account.CashFlowRole,
            CashFlowLineCode = account.CashFlowLineCode,
            account.IsContra,
            NegativeBalancePolicy = (short)account.NegativeBalancePolicy,
            IsActive = isActive
        };

        var cmd = new CommandDefinition(sql, p, transaction: uow.Transaction, cancellationToken: ct);
        await uow.Connection.ExecuteAsync(cmd);

        await ReplaceDimensionRulesAsync(account, ct);
    }

    public async Task UpdateAsync(Account account, bool isActive, CancellationToken ct = default)
    {
        await uow.EnsureOpenForTransactionAsync(ct);

        const string sql = """
                           UPDATE accounting_accounts
                           SET
                               code = @Code,
                               name = @Name,
                               account_type = @AccountType,
                               statement_section = @StatementSection,
                               cash_flow_role = @CashFlowRole,
                               cash_flow_line_code = @CashFlowLineCode,
                               is_contra = @IsContra,
                               negative_balance_policy = @NegativeBalancePolicy,
                               is_active = @IsActive,
                               updated_at_utc = NOW()
                           WHERE account_id = @AccountId AND is_deleted = FALSE;
                           """;

        var p = new
        {
            AccountId = account.Id,
            account.Code,
            account.Name,
            AccountType = (short)account.Type,
            StatementSection = (short)account.StatementSection,
            CashFlowRole = (short)account.CashFlowRole,
            CashFlowLineCode = account.CashFlowLineCode,
            account.IsContra,
            NegativeBalancePolicy = (short)account.NegativeBalancePolicy,
            IsActive = isActive
        };

        var cmd = new CommandDefinition(sql, p, transaction: uow.Transaction, cancellationToken: ct);
        await uow.Connection.ExecuteAsync(cmd);

        await ReplaceDimensionRulesAsync(account, ct);
    }

    public async Task SetActiveAsync(Guid accountId, bool isActive, CancellationToken ct = default)
    {
        await uow.EnsureOpenForTransactionAsync(ct);

        const string sql = """
                           UPDATE accounting_accounts
                           SET is_active = @IsActive,
                               updated_at_utc = NOW()
                           WHERE account_id = @AccountId AND is_deleted = FALSE;
                           """;

        var cmd = new CommandDefinition(sql, new { AccountId = accountId, IsActive = isActive }, transaction: uow.Transaction, cancellationToken: ct);
        await uow.Connection.ExecuteAsync(cmd);
    }

    public async Task MarkForDeletionAsync(Guid accountId, CancellationToken ct = default)
    {
        await uow.EnsureOpenForTransactionAsync(ct);

        const string sql = """
                           UPDATE accounting_accounts
                           SET is_deleted = TRUE,
                               is_active = FALSE,
                               updated_at_utc = NOW()
                           WHERE account_id = @AccountId AND is_deleted = FALSE;
                           """;

        var cmd = new CommandDefinition(sql, new { AccountId = accountId }, transaction: uow.Transaction, cancellationToken: ct);
        await uow.Connection.ExecuteAsync(cmd);
    }

    public async Task UnmarkForDeletionAsync(Guid accountId, CancellationToken ct = default)
    {
        await uow.EnsureOpenForTransactionAsync(ct);

        const string sql = """
                           UPDATE accounting_accounts
                           SET is_deleted = FALSE,
                               updated_at_utc = NOW()
                           WHERE account_id = @AccountId AND is_deleted = TRUE;
                           """;

        var cmd = new CommandDefinition(sql, new { AccountId = accountId }, transaction: uow.Transaction, cancellationToken: ct);
        await uow.Connection.ExecuteAsync(cmd);
    }

    private static Account ToAccount(AccountRow r, IReadOnlyList<AccountDimensionRule>? dimensionRules)
    {
        return new Account(
            r.AccountId,
            r.Code,
            r.Name,
            (AccountType)r.AccountType,
            statementSection: (StatementSection)r.StatementSection,
            negativeBalancePolicy: (NegativeBalancePolicy)r.NegativeBalancePolicy,
            isContra: r.IsContra,
            dimensionRules: dimensionRules,
            cashFlowRole: (CashFlowRole)r.CashFlowRole,
            cashFlowLineCode: r.CashFlowLineCode);
    }

    private async Task<Dictionary<Guid, IReadOnlyList<AccountDimensionRule>>> LoadDimensionRulesAsync(
        Guid[] accountIds,
        CancellationToken ct)
    {
        if (accountIds.Length == 0)
            return new Dictionary<Guid, IReadOnlyList<AccountDimensionRule>>();

        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
                           SELECT
                               r.account_id   AS "AccountId",
                               r.dimension_id AS "DimensionId",
                               d.code         AS "DimensionCode",
                               r.is_required  AS "IsRequired",
                               r.ordinal      AS "Ordinal"
                           FROM accounting_account_dimension_rules r
                           JOIN platform_dimensions d ON d.dimension_id = r.dimension_id
                           WHERE r.account_id = ANY(@AccountIds)
                           ORDER BY r.account_id, r.ordinal, d.code;
                           """;

        var cmd = new CommandDefinition(sql, new { AccountIds = accountIds }, transaction: uow.Transaction, cancellationToken: ct);
        var rows = await uow.Connection.QueryAsync<DimensionRuleRow>(cmd);

        var map = new Dictionary<Guid, List<AccountDimensionRule>>();
        foreach (var row in rows)
        {
            if (!map.TryGetValue(row.AccountId, out var list))
            {
                list = new List<AccountDimensionRule>();
                map.Add(row.AccountId, list);
            }

            list.Add(new AccountDimensionRule(
                row.DimensionId,
                row.DimensionCode,
                row.Ordinal,
                row.IsRequired));
        }

        return map.ToDictionary(
            kvp => kvp.Key, IReadOnlyList<AccountDimensionRule> (kvp) => kvp.Value.OrderBy(x => x.Ordinal).ToArray());
    }

    private async Task ReplaceDimensionRulesAsync(Account account, CancellationToken ct)
    {
        // Dimension rules are the source of truth for analytical dimensions.

        if (account.DimensionRules.Count == 0)
        {
            // Keep table clean: if the account declares no rules, remove any stale rows.
            const string deleteSql = "DELETE FROM accounting_account_dimension_rules WHERE account_id = @AccountId;";
            var deleteCmd = new CommandDefinition(deleteSql, new { AccountId = account.Id }, transaction: uow.Transaction, cancellationToken: ct);
            await uow.Connection.ExecuteAsync(deleteCmd);
            return;
        }

        // 1) Ensure referenced dimensions exist.
        const string upsertDimensionsSql = """
                                          INSERT INTO platform_dimensions(
                                              dimension_id,
                                              code,
                                              name,
                                              is_active,
                                              is_deleted,
                                              created_at_utc,
                                              updated_at_utc
                                          )
                                          SELECT
                                              x.dimension_id,
                                              x.code,
                                              x.name,
                                              TRUE,
                                              FALSE,
                                              NOW(),
                                              NOW()
                                          FROM UNNEST(@DimensionIds::uuid[], @Codes::text[], @Names::text[]) AS x(dimension_id, code, name)
                                          ON CONFLICT (dimension_id) DO NOTHING;
                                          """;

        var dimIds = account.DimensionRules.Select(x => x.DimensionId).ToArray();
        var dimCodes = account.DimensionRules.Select(x => x.DimensionCode.Trim()).ToArray();
        var dimNames = account.DimensionRules.Select(x => x.DimensionCode.Trim()).ToArray();

        var dimCmd = new CommandDefinition(
            upsertDimensionsSql,
            new { DimensionIds = dimIds, Codes = dimCodes, Names = dimNames },
            transaction: uow.Transaction,
            cancellationToken: ct);
        await uow.Connection.ExecuteAsync(dimCmd);

        // 2) Replace rules for the account.
        const string deleteRulesSql = "DELETE FROM accounting_account_dimension_rules WHERE account_id = @AccountId;";
        var deleteRulesCmd = new CommandDefinition(deleteRulesSql, new { AccountId = account.Id }, transaction: uow.Transaction, cancellationToken: ct);
        await uow.Connection.ExecuteAsync(deleteRulesCmd);

        const string insertRulesSql = """
                                      INSERT INTO accounting_account_dimension_rules(
                                          account_id,
                                          dimension_id,
                                          ordinal,
                                          is_required
                                      )
                                      SELECT
                                          @AccountId,
                                          x.dimension_id,
                                          x.ordinal,
                                          x.is_required
                                      FROM UNNEST(@DimensionIds::uuid[], @Ordinals::int4[], @Required::bool[]) AS x(dimension_id, ordinal, is_required);
                                      """;

        var insertCmd = new CommandDefinition(
            insertRulesSql,
            new
            {
                AccountId = account.Id,
                DimensionIds = account.DimensionRules.Select(x => x.DimensionId).ToArray(),
                Ordinals = account.DimensionRules.Select(x => x.Ordinal).ToArray(),
                Required = account.DimensionRules.Select(x => x.IsRequired).ToArray()
            },
            transaction: uow.Transaction,
            cancellationToken: ct);
        await uow.Connection.ExecuteAsync(insertCmd);
    }

    private sealed class AccountRow
    {
        public Guid AccountId { get; init; }
        public string Code { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public short AccountType { get; init; }
        public short StatementSection { get; init; }
        public short CashFlowRole { get; init; }
        public string? CashFlowLineCode { get; init; }

        public bool IsContra { get; init; }

        public short NegativeBalancePolicy { get; init; }

        public bool IsActive { get; init; }
        public bool IsDeleted { get; init; }
    }

    private sealed class DimensionRuleRow
    {
        public Guid AccountId { get; init; }
        public Guid DimensionId { get; init; }
        public string DimensionCode { get; init; } = string.Empty;
        public bool IsRequired { get; init; }
        public int Ordinal { get; init; }
    }
}
