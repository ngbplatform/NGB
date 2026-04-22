using Dapper;
using NGB.Metadata.Base;
using NGB.Persistence.Catalogs.Universal;
using NGB.Persistence.UnitOfWork;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.Catalogs;

internal sealed class PostgresCatalogWriter(IUnitOfWork uow) : ICatalogWriter
{
    public async Task UpsertHeadAsync(
        CatalogHeadDescriptor head,
        Guid catalogId,
        IReadOnlyList<CatalogHeadValue> values,
        CancellationToken ct = default)
        => await UpsertHeadsAsync(head, [new CatalogHeadWriteRow(catalogId, values)], ct);

    public async Task UpsertHeadsAsync(
        CatalogHeadDescriptor head,
        IReadOnlyList<CatalogHeadWriteRow> rows,
        CancellationToken ct = default)
    {
        if (rows is null)
            throw new NgbArgumentRequiredException(nameof(rows));

        if (rows.Count == 0)
            return;

        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        var normalizedRows = NormalizeRows(head, rows);
        if (normalizedRows.Columns.Count == 0)
            return;

        var insertColumns = new List<string> { "catalog_id" };
        insertColumns.AddRange(normalizedRows.Columns.Select(column => Qi(column.ColumnName)));

        var selectColumns = new List<string> { "x.catalog_id" };
        var updateSet = new List<string>();
        var unnestArgs = new List<string> { "@CatalogIds::uuid[]" };
        var unnestColumns = new List<string> { "catalog_id" };

        var p = new DynamicParameters();
        p.Add("CatalogIds", normalizedRows.CatalogIds);

        foreach (var column in normalizedRows.Columns)
        {
            selectColumns.Add(column.ColumnType == ColumnType.Json
                ? $"CASE WHEN x.{Qi(column.ColumnName)} IS NULL THEN NULL ELSE x.{Qi(column.ColumnName)}::jsonb END"
                : $"x.{Qi(column.ColumnName)}");
            updateSet.Add($"{Qi(column.ColumnName)} = EXCLUDED.{Qi(column.ColumnName)}");
            unnestArgs.Add($"@{column.ColumnName}::{ToArraySqlType(column.ColumnType)}");
            unnestColumns.Add(Qi(column.ColumnName));
            p.Add(column.ColumnName, normalizedRows.ColumnArrays[column.ColumnName]);
        }

        var sql = $"""
                  INSERT INTO {Qi(head.HeadTableName)} ({string.Join(", ", insertColumns)})
                  SELECT {string.Join(", ", selectColumns)}
                  FROM UNNEST({string.Join(", ", unnestArgs)}) AS x({string.Join(", ", unnestColumns)})
                  ON CONFLICT (catalog_id)
                  DO UPDATE SET {string.Join(", ", updateSet)};
                  """;

        await uow.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            p,
            transaction: uow.Transaction,
            cancellationToken: ct));
    }

    private static NormalizedCatalogHeadRows NormalizeRows(
        CatalogHeadDescriptor head,
        IReadOnlyList<CatalogHeadWriteRow> rows)
    {
        var headColumns = head.Columns
            .ToDictionary(column => column.ColumnName, StringComparer.OrdinalIgnoreCase);

        var rowMaps = new List<Dictionary<string, CatalogHeadValue>>(rows.Count);
        var usedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var catalogIds = new Guid[rows.Count];

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];

            if (row.CatalogId == Guid.Empty)
                throw new NgbArgumentRequiredException(nameof(rows));

            if (row.Values is null)
                throw new NgbArgumentRequiredException(nameof(rows));

            catalogIds[i] = row.CatalogId;

            var map = new Dictionary<string, CatalogHeadValue>(StringComparer.OrdinalIgnoreCase);

            foreach (var value in row.Values)
            {
                if (string.IsNullOrWhiteSpace(value.ColumnName))
                    throw new NgbArgumentInvalidException(nameof(rows), "ColumnName is required.");

                if (!headColumns.TryGetValue(value.ColumnName, out var headColumn))
                {
                    throw new NgbArgumentInvalidException(
                        nameof(rows),
                        $"Column '{value.ColumnName}' does not belong to catalog '{head.CatalogCode}'.");
                }

                if (headColumn.ColumnType != value.ColumnType)
                {
                    throw new NgbArgumentInvalidException(
                        nameof(rows),
                        $"Column '{value.ColumnName}' type mismatch. Expected '{headColumn.ColumnType}', got '{value.ColumnType}'.");
                }

                if (!map.TryAdd(value.ColumnName, value))
                    throw new NgbArgumentInvalidException(nameof(rows), $"Duplicate column '{value.ColumnName}' in the same catalog head row.");

                usedColumns.Add(value.ColumnName);
            }

            rowMaps.Add(map);
        }

        var columns = head.Columns
            .Where(column => usedColumns.Contains(column.ColumnName))
            .Select(column => new CatalogHeadColumn(column.ColumnName, column.ColumnType))
            .ToArray();

        var columnArrays = new Dictionary<string, Array>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in columns)
        {
            columnArrays[column.ColumnName] = CreateColumnArray(column.ColumnType, rows.Count);
        }

        for (var rowIndex = 0; rowIndex < rowMaps.Count; rowIndex++)
        {
            var map = rowMaps[rowIndex];

            foreach (var column in columns)
            {
                var array = columnArrays[column.ColumnName];
                var raw = map.TryGetValue(column.ColumnName, out var value) ? value.Value : null;
                SetArrayValue(array, rowIndex, raw, column.ColumnType);
            }
        }

        return new NormalizedCatalogHeadRows(catalogIds, columns, columnArrays);
    }

    private static Array CreateColumnArray(ColumnType columnType, int length)
        => columnType switch
        {
            ColumnType.String => new string?[length],
            ColumnType.Json => new string?[length],
            ColumnType.Guid => new Guid?[length],
            ColumnType.Int32 => new int?[length],
            ColumnType.Int64 => new long?[length],
            ColumnType.Decimal => new decimal?[length],
            ColumnType.Boolean => new bool?[length],
            ColumnType.Date => new DateOnly?[length],
            ColumnType.DateTimeUtc => new DateTime?[length],
            _ => throw new NgbArgumentInvalidException(nameof(columnType), $"Unsupported catalog column type '{columnType}'.")
        };

    private static void SetArrayValue(Array array, int index, object? value, ColumnType columnType)
    {
        switch (columnType)
        {
            case ColumnType.String:
            case ColumnType.Json:
                ((string?[])array)[index] = value as string;
                return;
            case ColumnType.Guid:
                ((Guid?[])array)[index] = (Guid?)value;
                return;
            case ColumnType.Int32:
                ((int?[])array)[index] = (int?)value;
                return;
            case ColumnType.Int64:
                ((long?[])array)[index] = (long?)value;
                return;
            case ColumnType.Decimal:
                ((decimal?[])array)[index] = (decimal?)value;
                return;
            case ColumnType.Boolean:
                ((bool?[])array)[index] = (bool?)value;
                return;
            case ColumnType.Date:
                ((DateOnly?[])array)[index] = (DateOnly?)value;
                return;
            case ColumnType.DateTimeUtc:
                ((DateTime?[])array)[index] = (DateTime?)value;
                return;
            default:
                throw new NgbArgumentInvalidException(nameof(columnType), $"Unsupported catalog column type '{columnType}'.");
        }
    }

    private static string ToArraySqlType(ColumnType columnType)
        => columnType switch
        {
            ColumnType.String => "text[]",
            ColumnType.Json => "text[]",
            ColumnType.Guid => "uuid[]",
            ColumnType.Int32 => "integer[]",
            ColumnType.Int64 => "bigint[]",
            ColumnType.Decimal => "numeric[]",
            ColumnType.Boolean => "boolean[]",
            ColumnType.Date => "date[]",
            ColumnType.DateTimeUtc => "timestamptz[]",
            _ => throw new NgbArgumentInvalidException(nameof(columnType), $"Unsupported catalog column type '{columnType}'.")
        };

    private sealed record NormalizedCatalogHeadRows(
        Guid[] CatalogIds,
        IReadOnlyList<CatalogHeadColumn> Columns,
        IReadOnlyDictionary<string, Array> ColumnArrays);

    private static string Qi(string ident)
    {
        if (string.IsNullOrWhiteSpace(ident))
            throw new NgbArgumentInvalidException(nameof(ident), "Identifier is required.");

        // Identifiers are sourced from trusted metadata (Definitions). Quote defensively.
        return '"' + ident.Replace("\"", "\"\"") + '"';
    }
}
