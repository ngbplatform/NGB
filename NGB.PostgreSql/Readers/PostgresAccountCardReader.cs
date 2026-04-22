using Dapper;
using System.Text;
using NGB.Accounting.Reports.AccountCard;
using NGB.Core.Dimensions;
using NGB.Core.Dimensions.Enrichment;
using NGB.Persistence.Dimensions;
using NGB.Persistence.Dimensions.Enrichment;
using NGB.Persistence.Readers.Reports;
using NGB.Persistence.UnitOfWork;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PostgreSql.Readers;

public sealed class PostgresAccountCardReader(
    IUnitOfWork uow,
    IDimensionSetReader dimensionSetReader,
    IDimensionValueEnrichmentReader dimensionValueEnrichmentReader)
    : IAccountCardReader, IAccountCardPageReader
{
    public async Task<IReadOnlyList<AccountCardLine>> GetAsync(
        Guid accountId,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        DimensionScopeBag? dimensionScopes = null,
        CancellationToken ct = default)
    {
        if (accountId == Guid.Empty)
            throw new NgbArgumentRequiredException(nameof(accountId));

        if (toInclusive < fromInclusive)
            throw new NgbArgumentOutOfRangeException(nameof(toInclusive), toInclusive, "To must be on or after From.");

        fromInclusive.EnsureMonthStart(nameof(fromInclusive));
        toInclusive.EnsureMonthStart(nameof(toInclusive));

        await uow.EnsureConnectionOpenAsync(ct);

        var (scopeDimIds, scopeValueIds, scopeDimensionCount) = SqlDimensionFilter.NormalizeScopes(dimensionScopes);
        var sql = BuildLinesSql(
            hasDimensionScopes: scopeDimensionCount > 0,
            hasCursor: false,
            paged: false);

        var rows = (await uow.Connection.QueryAsync<AccountCardLine>(
            new CommandDefinition(
                sql,
                new
                {
                    AccountId = accountId,
                    FromUtc = ToMonthStartUtc(fromInclusive),
                    ToExclusiveUtc = ToMonthStartUtc(toInclusive.AddMonths(1)),
                    ScopeDimensionCount = scopeDimensionCount,
                    ScopeDimIds = scopeDimIds,
                    ScopeValueIds = scopeValueIds
                },
                uow.Transaction,
                cancellationToken: ct))).AsList();

        await ResolveDimensionsAsync(rows, ct);
        await ResolveDimensionValueDisplaysAsync(rows, ct);

        return rows;
    }

    public async Task<AccountCardLinePage> GetPageAsync(
        AccountCardLinePageRequest request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new NgbArgumentRequiredException(nameof(request));

        if (request.AccountId == Guid.Empty)
            throw new NgbArgumentRequiredException(nameof(request.AccountId));

        if (request.ToInclusive < request.FromInclusive)
            throw new NgbArgumentOutOfRangeException(nameof(request.ToInclusive), request.ToInclusive, "To must be on or after From.");

        request.FromInclusive.EnsureMonthStart(nameof(request.FromInclusive));
        request.ToInclusive.EnsureMonthStart(nameof(request.ToInclusive));

        await uow.EnsureConnectionOpenAsync(ct);

        var (scopeDimIds, scopeValueIds, scopeDimensionCount) = SqlDimensionFilter.NormalizeScopes(request.DimensionScopes);
        var sql = BuildLinesSql(
            hasDimensionScopes: scopeDimensionCount > 0,
            hasCursor: request.Cursor is not null,
            paged: true);

        var rows = (await uow.Connection.QueryAsync<AccountCardLine>(
            new CommandDefinition(
                sql,
                new
                {
                    request.AccountId,
                    FromUtc = ToMonthStartUtc(request.FromInclusive),
                    ToExclusiveUtc = ToMonthStartUtc(request.ToInclusive.AddMonths(1)),
                    ScopeDimensionCount = scopeDimensionCount,
                    ScopeDimIds = scopeDimIds,
                    ScopeValueIds = scopeValueIds,
                    request.Cursor?.AfterPeriodUtc,
                    request.Cursor?.AfterEntryId,
                    LimitPlusOne = request.PageSize + 1
                },
                uow.Transaction,
                cancellationToken: ct))).AsList();

        var hasMore = rows.Count > request.PageSize;
        if (hasMore)
            rows.RemoveAt(rows.Count - 1);

        await ResolveDimensionsAsync(rows, ct);
        await ResolveDimensionValueDisplaysAsync(rows, ct);

        return new AccountCardLinePage
        {
            Lines = rows,
            HasMore = hasMore,
            NextCursor = BuildNextCursor(rows, hasMore)
        };
    }

    private async Task ResolveDimensionsAsync(IReadOnlyList<AccountCardLine> lines, CancellationToken ct)
    {
        if (lines.Count == 0)
            return;

        var ids = lines
            .SelectMany(x => new[] { x.DimensionSetId, x.CounterAccountDimensionSetId })
            .Distinct()
            .ToArray();

        var bags = await dimensionSetReader.GetBagsByIdsAsync(ids, ct);

        foreach (var line in lines)
        {
            line.Dimensions = bags.TryGetValue(line.DimensionSetId, out var primary)
                ? primary
                : DimensionBag.Empty;

            line.CounterAccountDimensions = bags.TryGetValue(line.CounterAccountDimensionSetId, out var counter)
                ? counter
                : DimensionBag.Empty;
        }
    }

    private async Task ResolveDimensionValueDisplaysAsync(IReadOnlyList<AccountCardLine> lines, CancellationToken ct)
    {
        if (lines.Count == 0)
            return;

        var keys = lines
            .SelectMany(x => new[] { x.Dimensions, x.CounterAccountDimensions })
            .CollectValueKeys();

        if (keys.Count == 0)
            return;

        var resolved = await dimensionValueEnrichmentReader.ResolveAsync(keys, ct);

        foreach (var line in lines)
        {
            line.DimensionValueDisplays = line.Dimensions.ToValueDisplayMap(resolved);
            line.CounterAccountDimensionValueDisplays = line.CounterAccountDimensions.ToValueDisplayMap(resolved);
        }
    }

    private static AccountCardLineCursor? BuildNextCursor(IReadOnlyList<AccountCardLine> lines, bool hasMore)
    {
        if (!hasMore || lines.Count == 0)
            return null;

        var last = lines[^1];
        return new AccountCardLineCursor
        {
            AfterPeriodUtc = last.PeriodUtc,
            AfterEntryId = last.EntryId
        };
    }

    private static string BuildLinesSql(bool hasDimensionScopes, bool hasCursor, bool paged)
    {
        var sql = new StringBuilder();

        sql.AppendLine("WITH");

        if (hasDimensionScopes)
        {
            sql.AppendLine("""
                           requested_scope_pairs AS (
                               SELECT *
                               FROM unnest(CAST(@ScopeDimIds AS uuid[]), CAST(@ScopeValueIds AS uuid[])) AS sp(dimension_id, value_id)
                           ),
                           matching_dimension_sets AS (
                               SELECT di.dimension_set_id
                               FROM platform_dimension_set_items di
                               JOIN requested_scope_pairs sp
                                 ON sp.dimension_id = di.dimension_id
                                AND sp.value_id = di.value_id
                               GROUP BY di.dimension_set_id
                               HAVING COUNT(DISTINCT di.dimension_id) = @ScopeDimensionCount
                           ),
                           """);
        }

        sql.AppendLine("""
                       me AS (
                           SELECT account_id, code
                           FROM accounting_accounts
                           WHERE account_id = CAST(@AccountId AS uuid) AND is_deleted = FALSE
                       ),
                       lines AS (
                           SELECT
                               r.entry_id AS "EntryId",
                               r.period AS "PeriodUtc",
                               r.document_id AS "DocumentId",
                               me.account_id AS "AccountId",
                               me.code AS "AccountCode",
                               ac.account_id AS "CounterAccountId",
                               ac.code AS "CounterAccountCode",
                               r.credit_dimension_set_id AS "CounterAccountDimensionSetId",
                               r.debit_dimension_set_id AS "DimensionSetId",
                               r.amount AS "DebitAmount",
                               0::numeric AS "CreditAmount"
                           FROM accounting_register_main r
                           CROSS JOIN me
                           JOIN accounting_accounts ac ON ac.account_id = r.credit_account_id AND ac.is_deleted = FALSE
                       """);

        if (hasDimensionScopes)
        {
            sql.AppendLine("""
                           JOIN matching_dimension_sets debit_scope
                             ON debit_scope.dimension_set_id = r.debit_dimension_set_id
                           """);
        }

        sql.AppendLine("""
                           WHERE
                               r.debit_account_id = me.account_id
                               AND r.period >= CAST(@FromUtc AS timestamptz)
                               AND r.period < CAST(@ToExclusiveUtc AS timestamptz)
                       """);

        if (hasCursor)
            sql.AppendLine("""      AND (r.period, r.entry_id) > (CAST(@AfterPeriodUtc AS timestamptz), @AfterEntryId)""");

        sql.AppendLine("""

                           UNION ALL

                           SELECT
                               r.entry_id AS "EntryId",
                               r.period AS "PeriodUtc",
                               r.document_id AS "DocumentId",
                               me.account_id AS "AccountId",
                               me.code AS "AccountCode",
                               ad.account_id AS "CounterAccountId",
                               ad.code AS "CounterAccountCode",
                               r.debit_dimension_set_id AS "CounterAccountDimensionSetId",
                               r.credit_dimension_set_id AS "DimensionSetId",
                               0::numeric AS "DebitAmount",
                               r.amount AS "CreditAmount"
                           FROM accounting_register_main r
                           CROSS JOIN me
                           JOIN accounting_accounts ad ON ad.account_id = r.debit_account_id AND ad.is_deleted = FALSE
                       """);

        if (hasDimensionScopes)
        {
            sql.AppendLine("""
                           JOIN matching_dimension_sets credit_scope
                             ON credit_scope.dimension_set_id = r.credit_dimension_set_id
                           """);
        }

        sql.AppendLine("""
                           WHERE
                               r.credit_account_id = me.account_id
                               AND r.period >= CAST(@FromUtc AS timestamptz)
                               AND r.period < CAST(@ToExclusiveUtc AS timestamptz)
                       """);

        if (hasCursor)
            sql.AppendLine("""      AND (r.period, r.entry_id) > (CAST(@AfterPeriodUtc AS timestamptz), @AfterEntryId)""");

        sql.AppendLine("""
                       )
                       SELECT *
                       FROM lines
                       ORDER BY "PeriodUtc", "EntryId"
                       """);

        if (paged)
            sql.AppendLine("LIMIT @LimitPlusOne;");
        else
            sql.AppendLine(";");

        return sql.ToString();
    }

    private static DateTime ToMonthStartUtc(DateOnly period)
        => new(period.Year, period.Month, period.Day, 0, 0, 0, DateTimeKind.Utc);
}
