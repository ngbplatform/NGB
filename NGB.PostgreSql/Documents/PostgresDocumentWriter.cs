using Dapper;
using NGB.Metadata.Base;
using NGB.Persistence.Documents.Universal;
using NGB.Persistence.UnitOfWork;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.Documents;

internal sealed class PostgresDocumentWriter(IUnitOfWork uow) : IDocumentWriter
{
    public async Task UpsertHeadAsync(
        DocumentHeadDescriptor head,
        Guid documentId,
        IReadOnlyList<DocumentHeadValue> values,
        CancellationToken ct = default)
    {
        if (documentId == Guid.Empty)
            throw new NgbArgumentRequiredException(nameof(documentId));

        if (values is null)
            throw new NgbArgumentRequiredException(nameof(values));

        if (values.Count == 0)
            return;

        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        var insertColumns = new List<string> { "document_id" };
        var insertValues = new List<string> { "@documentId" };
        var updateSet = new List<string>();

        var p = new DynamicParameters();
        p.Add("documentId", documentId);

        foreach (var v in values)
        {
            if (string.IsNullOrWhiteSpace(v.ColumnName))
                throw new NgbArgumentInvalidException(nameof(values), "ColumnName is required.");

            insertColumns.Add(Qi(v.ColumnName));

            if (v.ColumnType == ColumnType.Json)
            {
                insertValues.Add($"CAST(@{v.ColumnName} AS jsonb)");
                updateSet.Add($"{Qi(v.ColumnName)} = CAST(@{v.ColumnName} AS jsonb)");
            }
            else
            {
                insertValues.Add($"@{v.ColumnName}");
                updateSet.Add($"{Qi(v.ColumnName)} = @{v.ColumnName}");
            }

            p.Add(v.ColumnName, v.Value);
        }

        var sql = $"""
                  INSERT INTO {Qi(head.HeadTableName)} ({string.Join(", ", insertColumns)})
                  VALUES ({string.Join(", ", insertValues)})
                  ON CONFLICT (document_id)
                  DO UPDATE SET {string.Join(", ", updateSet)};
                  """;

        await uow.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            p,
            transaction: uow.Transaction,
            cancellationToken: ct));
    }

    private static string Qi(string ident)
    {
        if (string.IsNullOrWhiteSpace(ident))
            throw new NgbArgumentInvalidException(nameof(ident), "Identifier is required.");

        // Identifiers are sourced from trusted metadata (Definitions). Quote defensively.
        return '"' + ident.Replace("\"", "\"\"") + '"';
    }
}
