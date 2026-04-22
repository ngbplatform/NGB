using System.Text.RegularExpressions;
using Dapper;
using NGB.Persistence.Documents.Storage;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Internal;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.Documents;

/// <summary>
/// Reusable per-type document storage for the common case: a single head table (doc_*)
/// with a required <c>document_id</c> FK and a few scalar columns.
///
/// If a document has parts tables or custom semantics, implement <see cref="IDocumentTypeStorage"/> manually.
/// </summary>
public sealed class PostgresHeadDocumentTypeStorage(
    IUnitOfWork uow,
    string typeCode,
    string headTable,
    IReadOnlyList<PostgresHeadDocumentTypeStorage.Column> columns)
    : IDocumentTypeStorage
{
    private static readonly Regex SafeParameter = new(
        "^[a-z_][a-z0-9_]*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string TypeCode { get; } = typeCode;

    private readonly string _insertSql = BuildInsertSql(headTable, columns);
    private readonly string _deleteSql = BuildDeleteSql(headTable);

    public async Task CreateDraftAsync(Guid documentId, CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();

        var p = new DynamicParameters();
        p.Add("documentId", documentId);

        foreach (var c in columns)
        {
            p.Add(c.ParameterName, c.ValueFactory(documentId));
        }

        await uow.Connection.ExecuteAsync(new CommandDefinition(
            _insertSql,
            p,
            uow.Transaction,
            cancellationToken: ct));
    }

    public async Task DeleteDraftAsync(Guid documentId, CancellationToken ct = default)
    {
        uow.EnsureActiveTransaction();
        await uow.Connection.ExecuteAsync(new CommandDefinition(
            _deleteSql,
            new { documentId },
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
        PostgresSqlIdentifiers.EnsureOrThrow(headTable, "PostgresHeadDocumentTypeStorage.headTable");

        foreach (var c in columns)
        {
            PostgresSqlIdentifiers.EnsureOrThrow(c.ColumnName, "PostgresHeadDocumentTypeStorage.column");

            if (string.IsNullOrWhiteSpace(c.ParameterName) || !SafeParameter.IsMatch(c.ParameterName))
                throw new NgbConfigurationViolationException(
                    $"Unsafe SQL parameter name '{c.ParameterName}'. Must match '{SafeParameter}'.",
                    new Dictionary<string, object?>
                    {
                        ["parameter"] = c.ParameterName,
                        ["regex"] = SafeParameter.ToString()
                    });
        }

        var columnList = "document_id";
        var valuesList = "@documentId";

        if (columns.Count > 0)
        {
            columnList += ", " + string.Join(", ", columns.Select(x => x.ColumnName));
            valuesList += ", " + string.Join(", ", columns.Select(x => "@" + x.ParameterName));
        }

        return $"INSERT INTO {headTable}({columnList}) VALUES ({valuesList}) ON CONFLICT (document_id) DO NOTHING;";
    }

    private static string BuildDeleteSql(string headTable)
    {
        PostgresSqlIdentifiers.EnsureOrThrow(headTable, "PostgresHeadDocumentTypeStorage.headTable");
        return $"DELETE FROM {headTable} WHERE document_id = @documentId;";
    }
}
