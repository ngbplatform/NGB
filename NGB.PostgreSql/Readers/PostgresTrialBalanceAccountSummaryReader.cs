using System.Runtime.CompilerServices;
using Dapper;
using NGB.Accounting.Accounts;
using NGB.Core.Dimensions;
using NGB.Persistence.Readers.Reports;
using NGB.Persistence.UnitOfWork;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PostgreSql.Readers;

public sealed class PostgresTrialBalanceAccountSummaryReader(IUnitOfWork uow) : ITrialBalanceAccountSummaryReader
{
    public async IAsyncEnumerable<TrialBalanceAccountSummary> ReadAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        DimensionScopeBag? dimensionScopes,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        fromInclusive.EnsureMonthStart(nameof(fromInclusive));
        toInclusive.EnsureMonthStart(nameof(toInclusive));

        if (toInclusive < fromInclusive)
            throw new NgbArgumentInvalidException(nameof(toInclusive), "To must be on or after From.");

        var (dimensionIds, valueIds, dimensionCount) = SqlDimensionFilter.NormalizeScopes(dimensionScopes);
        // One statement gives closed-period selection, balances, turnovers and labels the same MVCC snapshot.
        // Filter before aggregation; never transfer account/dimension combinations to the application.
        var sql = PostgresAccountSummarySql.Sql + " ORDER BY a.account_type, a.code COLLATE \"C\", a.account_id";

        await uow.EnsureConnectionOpenAsync(ct);

        await using var reader = await uow.Connection.ExecuteReaderAsync(new CommandDefinition(
            sql,
            new { 
                FromInclusive = fromInclusive,
                ToInclusive = toInclusive,
                DimensionIds = dimensionIds,
                ValueIds = valueIds,
                DimensionCount = dimensionCount
            },
            transaction: uow.Transaction,
            commandTimeout: 300,
            cancellationToken: ct));

        while (await reader.ReadAsync(ct))
        {
            yield return new TrialBalanceAccountSummary(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                (AccountType)reader.GetInt16(3),
                reader.GetDecimal(4),
                reader.GetDecimal(5),
                reader.GetDecimal(6));
        }
    }
}
