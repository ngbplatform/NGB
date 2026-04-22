using System.Text.RegularExpressions;
using Dapper;
using NGB.Persistence.Catalogs.Storage;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Internal;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.Catalogs;

/// <summary>
/// Reusable per-type catalog storage for the common case: a single head table (cat_*)
/// with a required <c>catalog_id</c> FK and a few scalar columns.
///
/// If a catalog has parts tables or custom semantics, implement <see cref="ICatalogTypeStorage"/> manually.
/// </summary>
public sealed class PostgresHeadCatalogTypeStorage(
    IUnitOfWork uow,
    string catalogCode,
    string headTable,
    IReadOnlyList<PostgresHeadCatalogTypeStorage.Column> columns)
    : ICatalogTypeStorage
{
    private static readonly Regex SafeParameter = new(
        "^[a-z_][a-z0-9_]*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string CatalogCode { get; } = catalogCode;

    private readonly string _insertSql = BuildInsertSql(headTable, columns);
    private readonly string _deleteSql = BuildDeleteSql(headTable);

    public async Task EnsureCreatedAsync(Guid catalogId, CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();

        var p = new DynamicParameters();
        p.Add("catalogId", catalogId);

        foreach (var c in columns)
        {
            p.Add(c.ParameterName, c.ValueFactory(catalogId));
        }

        await uow.Connection.ExecuteAsync(new CommandDefinition(
            _insertSql,
            p,
            uow.Transaction,
            cancellationToken: ct));
    }

    public async Task DeleteAsync(Guid catalogId, CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.Connection.ExecuteAsync(new CommandDefinition(
            _deleteSql,
            new { catalogId },
            uow.Transaction,
            cancellationToken: ct));
    }

    public sealed record Column(string ColumnName, string ParameterName, Func<Guid, object?> ValueFactory)
    {
        public static Column DraftString(
            string columnName,
            string parameterName,
            string prefix = "DRAFT-",
            string guidFormat = "N")
            => new(columnName, parameterName, id => $"{prefix}{id.ToString(guidFormat)}");
    }

    private static string BuildInsertSql(string headTable, IReadOnlyList<Column> columns)
    {
        PostgresSqlIdentifiers.EnsureOrThrow(headTable, "PostgresHeadCatalogTypeStorage.headTable");

        foreach (var c in columns)
        {
            PostgresSqlIdentifiers.EnsureOrThrow(c.ColumnName, "PostgresHeadCatalogTypeStorage.column");

            if (string.IsNullOrWhiteSpace(c.ParameterName) || !SafeParameter.IsMatch(c.ParameterName))
                throw new NgbConfigurationViolationException(
                    $"Unsafe SQL parameter name '{c.ParameterName}'. Must match '{SafeParameter}'.",
                    new Dictionary<string, object?>
                    {
                        ["parameter"] = c.ParameterName,
                        ["regex"] = SafeParameter.ToString()
                    });
        }

        var columnList = "catalog_id";
        var valuesList = "@catalogId";

        if (columns.Count > 0)
        {
            columnList += ", " + string.Join(", ", columns.Select(x => x.ColumnName));
            valuesList += ", " + string.Join(", ", columns.Select(x => "@" + x.ParameterName));
        }

        return $"INSERT INTO {headTable}({columnList}) VALUES ({valuesList}) ON CONFLICT (catalog_id) DO NOTHING;";
    }

    private static string BuildDeleteSql(string headTable)
    {
        PostgresSqlIdentifiers.EnsureOrThrow(headTable, "PostgresHeadCatalogTypeStorage.headTable");
        return $"DELETE FROM {headTable} WHERE catalog_id = @catalogId;";
    }
}
