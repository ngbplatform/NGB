using System.Security.Cryptography;
using System.Text;
using Dapper;
using NGB.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.OperationalRegisters.Exceptions;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Internal;
using NGB.PostgreSql.UnitOfWork;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.OperationalRegisters;

/// <summary>
/// Shared implementation for per-register monthly projection stores (turnovers/balances).
///
/// Both projections use the same physical shape:
/// (period_month, dimension_set_id, <dynamic resource numeric columns>) with replace-per-month semantics.
/// </summary>
internal sealed class PostgresOperationalRegisterMonthlyProjectionStoreCore(
    IUnitOfWork uow,
    IOperationalRegisterRepository registers,
    IOperationalRegisterResourceRepository resourcesRepo,
    Func<string, string> tableNameFactory,
    string tableNameDescription,
    string indexPrefix,
    bool aliasResourceColumns)
{
    public async Task EnsureSchemaAsync(Guid registerId, CancellationToken ct = default)
    {
        if (registerId == Guid.Empty)
            throw new NgbArgumentInvalidException(nameof(registerId), "RegisterId must not be empty.");

        await uow.EnsureConnectionOpenAsync(ct);

        await using var schemaLock = await PostgresOperationalRegisterSchemaLock.AcquireAsync(uow, registerId, ct);

        var (table, resources) = await ResolveTableAndResourcesOrThrowAsync(registerId, ct);

        // Base table (derived data: replace semantics per month => NOT append-only).
        await uow.Connection.ExecuteAsync($"""
CREATE TABLE IF NOT EXISTS {table}(
    period_month DATE NOT NULL,
    dimension_set_id UUID NOT NULL DEFAULT '{Guid.Empty}',
    FOREIGN KEY (dimension_set_id) REFERENCES platform_dimension_sets(dimension_set_id),
    UNIQUE (period_month, dimension_set_id)
);
""", transaction: uow.Transaction);

        // Resource columns (NUMERIC(28,8) NOT NULL DEFAULT 0).
        foreach (var r in resources)
        {
            await uow.Connection.ExecuteAsync(
                $"ALTER TABLE {table} ADD COLUMN IF NOT EXISTS {r.ColumnCode} NUMERIC(28,8) NOT NULL DEFAULT 0;",
                transaction: uow.Transaction);
        }

        // Indexes
        // NOTE: avoid string literals inside interpolation (would break C# parsing).
        var ixMonth = Ix(table, "month");
        var ixDim = Ix(table, "dim");

        await uow.Connection.ExecuteAsync(
            $"CREATE INDEX IF NOT EXISTS {ixMonth} ON {table}(period_month);",
            transaction: uow.Transaction);

        await uow.Connection.ExecuteAsync(
            $"CREATE INDEX IF NOT EXISTS {ixDim} ON {table}(dimension_set_id);",
            transaction: uow.Transaction);
    }

    public async Task ReplaceForMonthAsync(
        Guid registerId,
        DateOnly periodMonth,
        IReadOnlyList<OperationalRegisterMonthlyProjectionRow> rows,
        CancellationToken ct = default)
    {
        if (rows is null)
            throw new NgbArgumentRequiredException(nameof(rows));

        if (registerId == Guid.Empty)
            throw new NgbArgumentInvalidException(nameof(registerId), "RegisterId must not be empty.");

        await uow.EnsureOpenForTransactionAsync(ct);

        await EnsureSchemaAsync(registerId, ct);
        var (table, resources) = await ResolveTableAndResourcesOrThrowAsync(registerId, ct);

        periodMonth = OperationalRegisterPeriod.MonthStart(periodMonth);

        // Replace semantics per month.
        await uow.Connection.ExecuteAsync(
            new CommandDefinition(
                $"DELETE FROM {table} WHERE period_month = @PeriodMonth;",
                new { PeriodMonth = periodMonth },
                transaction: uow.Transaction,
                cancellationToken: ct));

        if (rows.Count == 0)
            return;

        ValidateResourceKeys(registerId, resources, rows);

        var count = rows.Count;
        var dimensionSetIds = new Guid[count];

        for (var i = 0; i < count; i++)
        {
            dimensionSetIds[i] = rows[i].DimensionSetId;
        }

        // Insert.
        var cols = new List<string> { "period_month", "dimension_set_id" };
        cols.AddRange(resources.Select(r => r.ColumnCode));

        if (resources.Length == 0)
        {
            var sql = $"""
INSERT INTO {table} ({string.Join(", ", cols)})
SELECT @PeriodMonth, x.dimension_set_id
FROM UNNEST(@DimensionSetIds::uuid[]) AS x(dimension_set_id);
""";

            await uow.Connection.ExecuteAsync(
                new CommandDefinition(
                    sql,
                    new { PeriodMonth = periodMonth, DimensionSetIds = dimensionSetIds },
                    transaction: uow.Transaction,
                    cancellationToken: ct));

            return;
        }

        var resourceArrays = BuildResourceArrays(resources, rows);

        var unnestCols = new List<string> { "dimension_set_id" };
        unnestCols.AddRange(resources.Select(r => r.ColumnCode));

        var unnestArgs = new List<string> { "@DimensionSetIds::uuid[]" };
        unnestArgs.AddRange(resources.Select(r => $"@{Param(r.ColumnCode)}::numeric[]"));

        var selectCols = new List<string> { "@PeriodMonth", "x.dimension_set_id" };
        selectCols.AddRange(resources.Select(r => $"x.{r.ColumnCode}"));

        var sqlInsert = $"""
INSERT INTO {table} ({string.Join(", ", cols)})
SELECT {string.Join(", ", selectCols)}
FROM UNNEST({string.Join(", ", unnestArgs)}) AS x({string.Join(", ", unnestCols)});
""";

        var p = new DynamicParameters();
        p.Add("PeriodMonth", periodMonth);
        p.Add("DimensionSetIds", dimensionSetIds);

        foreach (var (paramName, values) in resourceArrays)
        {
            p.Add(paramName, values);
        }

        await uow.Connection.ExecuteAsync(sqlInsert, p, transaction: uow.Transaction);
    }

    public async Task<IReadOnlyList<OperationalRegisterMonthlyProjectionRow>> GetByMonthAsync(
        Guid registerId,
        DateOnly periodMonth,
        Guid? dimensionSetId = null,
        CancellationToken ct = default)
    {
        if (registerId == Guid.Empty)
            throw new NgbArgumentInvalidException(nameof(registerId), "RegisterId must not be empty.");

        await uow.EnsureConnectionOpenAsync(ct);

        var (table, resources) = await ResolveTableAndResourcesOrThrowAsync(registerId, ct);
        if (!await PostgresTableExistence.ExistsAsync(uow, table, ct))
            return [];

        periodMonth = OperationalRegisterPeriod.MonthStart(periodMonth);

        // Resource columns are created with stable, table-safe names (column_code).
        // Selecting them without aliases keeps the reader logic simple and avoids tricky quoting.
        var resourcesSelect = resources.Length == 0
            ? string.Empty
            : ", " + string.Join(", ", resources.Select(r =>
                aliasResourceColumns ? $"{r.ColumnCode} AS \"{r.ColumnCode}\"" : r.ColumnCode));

        var sql = $"""
SELECT
    dimension_set_id AS "DimensionSetId"{resourcesSelect}
FROM {table}
WHERE period_month = @PeriodMonth
  AND (@DimensionSetId IS NULL OR dimension_set_id = @DimensionSetId)
ORDER BY dimension_set_id;
""";

        var cmd = new CommandDefinition(
            sql,
            new { PeriodMonth = periodMonth, DimensionSetId = dimensionSetId },
            transaction: uow.Transaction,
            cancellationToken: ct);

        var rows = await uow.Connection.QueryAsync(cmd);

        var result = new List<OperationalRegisterMonthlyProjectionRow>();
        foreach (var row in rows)
        {
            var d = (IDictionary<string, object?>)row;

            var dimSetId = (Guid)d["DimensionSetId"]!;
            var values = new Dictionary<string, decimal>(StringComparer.Ordinal);

            foreach (var r in resources)
            {
                var v = d.TryGetValue(r.ColumnCode, out var obj) ? obj : null;
                values[r.ColumnCode] = (v is null || v is DBNull) ? 0m : Convert.ToDecimal(v);
            }

            result.Add(new OperationalRegisterMonthlyProjectionRow(dimSetId, values));
        }

        return result;
    }

    private async Task<(string TableName, OperationalRegisterResource[] Resources)> ResolveTableAndResourcesOrThrowAsync(
        Guid registerId,
        CancellationToken ct)
    {
        var reg = await registers.GetByIdAsync(registerId, ct);
        if (reg is null)
            throw new OperationalRegisterNotFoundException(registerId);

        var tableName = tableNameFactory(reg.TableCode);
        OperationalRegisterSqlIdentifiers.EnsureOrThrow(tableName, tableNameDescription);

        var resources = (await resourcesRepo.GetByRegisterIdAsync(registerId, ct))
            .OrderBy(r => r.Ordinal)
            .ToArray();

        foreach (var r in resources)
        {
            OperationalRegisterSqlIdentifiers.EnsureOrThrow(r.ColumnCode, "opreg resource column_code");
        }

        return (tableName, resources);
    }

    private static void ValidateResourceKeys(
        Guid registerId,
        OperationalRegisterResource[] resources,
        IReadOnlyList<OperationalRegisterMonthlyProjectionRow> rows)
    {
        if (resources.Length == 0)
            return;

        var allowed = new HashSet<string>(resources.Select(r => r.ColumnCode), StringComparer.Ordinal);

        for (var i = 0; i < rows.Count; i++)
        {
            foreach (var k in rows[i].Values.Keys)
            {
                if (!allowed.Contains(k))
                {
                    var reason = $"Unknown resource column {k}. Ensure it exists in operational_register_resources.";

                    throw new OperationalRegisterResourcesValidationException(
                        registerId,
                        reason: reason,
                        details: new Dictionary<string, object?>
                        {
                            ["rowIndex"] = i,
                            ["unknownKey"] = k
                        });
                }
            }
        }
    }

    private static List<(string ParamName, decimal[] Values)> BuildResourceArrays(
        OperationalRegisterResource[] resources,
        IReadOnlyList<OperationalRegisterMonthlyProjectionRow> rows)
    {
        var result = new List<(string ParamName, decimal[] Values)>(resources.Length);

        foreach (var r in resources)
        {
            var arr = new decimal[rows.Count];

            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row.Values.TryGetValue(r.ColumnCode, out var v))
                    arr[i] = v;
            }

            result.Add((Param(r.ColumnCode), arr));
        }

        return result;
    }

    private static string Param(string columnCode) => "p_" + columnCode;

    private string Ix(string table, string purpose)
        => indexPrefix + Hash8(table + "|" + purpose);

    private static string Hash8(string s)
        => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant()[..8];
}
