using Dapper;
using NGB.Accounting.Accounts;
using NGB.Accounting.Dimensions;
using NGB.Accounting.Registers;
using NGB.Core.Dimensions;
using NGB.Persistence.Dimensions;
using NGB.Persistence.Readers;
using NGB.Persistence.UnitOfWork;

namespace NGB.PostgreSql.Readers;

/// <summary>
/// PostgreSQL implementation of <see cref="IAccountingEntryReader"/>.
///
/// PERFORMANCE:
/// - This reader must NOT load the entire Chart of Accounts.
/// - We JOIN <c>accounting_accounts</c> to fetch the required account display/metadata per entry.
/// - Dimension rules are loaded on-demand for only the touched accounts.
/// </summary>
public sealed class PostgresAccountingEntryReader(IUnitOfWork uow, IDimensionSetReader dimensionSets)
    : IAccountingEntryReader
{
    public Task<IReadOnlyList<AccountingEntry>> GetByDocumentAsync(Guid documentId, CancellationToken ct = default)
        => GetByDocumentAsync(documentId, limit: int.MaxValue, ct);

    public async Task<IReadOnlyList<AccountingEntry>> GetByDocumentAsync(
        Guid documentId,
        int limit,
        CancellationToken ct = default)
    {
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
                           SELECT
                               e.entry_id               AS EntryId,
                               e.document_id            AS DocumentId,
                               e.period                 AS Period,

                               e.debit_dimension_set_id  AS DebitDimensionSetId,
                               e.credit_dimension_set_id AS CreditDimensionSetId,

                               e.debit_account_id       AS DebitAccountId,
                               da.code                  AS DebitAccountCode,
                               da.name                  AS DebitAccountName,
                               da.account_type          AS DebitAccountType,
                               da.statement_section     AS DebitStatementSection,
                               da.is_contra             AS DebitIsContra,
                               da.negative_balance_policy AS DebitNegativeBalancePolicy,

                               e.credit_account_id      AS CreditAccountId,
                               ca.code                  AS CreditAccountCode,
                               ca.name                  AS CreditAccountName,
                               ca.account_type          AS CreditAccountType,
                               ca.statement_section     AS CreditStatementSection,
                               ca.is_contra             AS CreditIsContra,
                               ca.negative_balance_policy AS CreditNegativeBalancePolicy,

                               e.amount                 AS Amount,
                               e.is_storno              AS IsStorno
                           FROM accounting_register_main e
                           JOIN accounting_accounts da ON da.account_id = e.debit_account_id
                           JOIN accounting_accounts ca ON ca.account_id = e.credit_account_id
                           WHERE e.document_id = @DocumentId
                           ORDER BY e.period, e.entry_id
                           LIMIT @Limit;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new { DocumentId = documentId, Limit = limit <= 0 ? 2147483647 : limit },
            transaction: uow.Transaction,
            cancellationToken: ct);

        var rows = (await uow.Connection.QueryAsync<AccountingEntryRow>(cmd)).AsList();

        if (rows.Count == 0)
            return [];

        var bags = await ResolveBagsAsync(rows, ct);
        var rules = await ResolveRulesByAccountAsync(rows, ct);

        return rows.Select(r =>
        {
            var debitRules = rules.TryGetValue(r.DebitAccountId, out var dr) ? dr : Array.Empty<AccountDimensionRule>();
            var creditRules = rules.TryGetValue(r.CreditAccountId, out var cr) ? cr : Array.Empty<AccountDimensionRule>();

            var debit = new Account(
                r.DebitAccountId,
                r.DebitAccountCode,
                r.DebitAccountName,
                (AccountType)r.DebitAccountType,
                statementSection: (StatementSection)r.DebitStatementSection,
                negativeBalancePolicy: (NegativeBalancePolicy)r.DebitNegativeBalancePolicy,
                isContra: r.DebitIsContra,
                dimensionRules: debitRules);

            var credit = new Account(
                r.CreditAccountId,
                r.CreditAccountCode,
                r.CreditAccountName,
                (AccountType)r.CreditAccountType,
                statementSection: (StatementSection)r.CreditStatementSection,
                negativeBalancePolicy: (NegativeBalancePolicy)r.CreditNegativeBalancePolicy,
                isContra: r.CreditIsContra,
                dimensionRules: creditRules);

            return new AccountingEntry
            {
                EntryId = r.EntryId,
                DocumentId = r.DocumentId,
                Period = r.Period,
                Debit = debit,
                Credit = credit,

                DebitDimensionSetId = r.DebitDimensionSetId,
                CreditDimensionSetId = r.CreditDimensionSetId,
                DebitDimensions = bags.TryGetValue(r.DebitDimensionSetId, out var db) ? db : DimensionBag.Empty,
                CreditDimensions = bags.TryGetValue(r.CreditDimensionSetId, out var cb) ? cb : DimensionBag.Empty,

                Amount = r.Amount,
                IsStorno = r.IsStorno
            };
        }).ToList();
    }

    private async Task<IReadOnlyDictionary<Guid, DimensionBag>> ResolveBagsAsync(
        IReadOnlyList<AccountingEntryRow> rows,
        CancellationToken ct)
    {
        var ids = rows
            .SelectMany(r => new[] { r.DebitDimensionSetId, r.CreditDimensionSetId })
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();

        if (ids.Length == 0)
            return new Dictionary<Guid, DimensionBag>();

        return await dimensionSets.GetBagsByIdsAsync(ids, ct);
    }

    private async Task<IReadOnlyDictionary<Guid, IReadOnlyList<AccountDimensionRule>>> ResolveRulesByAccountAsync(
        IReadOnlyList<AccountingEntryRow> rows,
        CancellationToken ct)
    {
        var accountIds = rows
            .SelectMany(r => new[] { r.DebitAccountId, r.CreditAccountId })
            .Distinct()
            .ToArray();

        if (accountIds.Length == 0)
            return new Dictionary<Guid, IReadOnlyList<AccountDimensionRule>>();

        const string sql = """
                           SELECT
                               r.account_id   AS AccountId,
                               r.dimension_id AS DimensionId,
                               d.code         AS DimensionCode,
                               r.is_required  AS IsRequired,
                               r.ordinal      AS Ordinal
                           FROM accounting_account_dimension_rules r
                           JOIN platform_dimensions d ON d.dimension_id = r.dimension_id
                           WHERE r.account_id = ANY(@AccountIds)
                           ORDER BY r.account_id, r.ordinal, d.code;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new { AccountIds = accountIds },
            transaction: uow.Transaction,
            cancellationToken: ct);

        var ruleRows = (await uow.Connection.QueryAsync<RuleRow>(cmd)).AsList();

        if (ruleRows.Count == 0)
            return new Dictionary<Guid, IReadOnlyList<AccountDimensionRule>>();

        var dict = new Dictionary<Guid, List<AccountDimensionRule>>();

        foreach (var rr in ruleRows)
        {
            if (!dict.TryGetValue(rr.AccountId, out var list))
            {
                list = new List<AccountDimensionRule>();
                dict.Add(rr.AccountId, list);
            }

            list.Add(new AccountDimensionRule(rr.DimensionId, rr.DimensionCode, rr.Ordinal, rr.IsRequired));
        }

        return dict.ToDictionary(x => x.Key, x => (IReadOnlyList<AccountDimensionRule>)x.Value);
    }

    private sealed class AccountingEntryRow
    {
        public long EntryId { get; init; }
        public Guid DocumentId { get; init; }
        public DateTime Period { get; init; }
        public Guid DebitDimensionSetId { get; init; }
        public Guid CreditDimensionSetId { get; init; }

        public Guid DebitAccountId { get; init; }
        public string DebitAccountCode { get; init; } = string.Empty;
        public string DebitAccountName { get; init; } = string.Empty;
        public short DebitAccountType { get; init; }
        public short DebitStatementSection { get; init; }
        public bool DebitIsContra { get; init; }
        public short DebitNegativeBalancePolicy { get; init; }

        public Guid CreditAccountId { get; init; }
        public string CreditAccountCode { get; init; } = string.Empty;
        public string CreditAccountName { get; init; } = string.Empty;
        public short CreditAccountType { get; init; }
        public short CreditStatementSection { get; init; }
        public bool CreditIsContra { get; init; }
        public short CreditNegativeBalancePolicy { get; init; }

        public decimal Amount { get; init; }
        public bool IsStorno { get; init; }
    }

    private sealed class RuleRow
    {
        public Guid AccountId { get; init; }
        public Guid DimensionId { get; init; }
        public string DimensionCode { get; init; } = string.Empty;
        public bool IsRequired { get; init; }
        public int Ordinal { get; init; }
    }
}
