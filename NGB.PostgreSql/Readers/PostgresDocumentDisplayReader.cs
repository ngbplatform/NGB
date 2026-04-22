using Dapper;
using NGB.Metadata.Base;
using NGB.Metadata.Documents.Storage;
using NGB.Persistence.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Documents;
using NGB.PostgreSql.Internal;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.Readers;

/// <summary>
/// Bulk document display resolver for reports and UI widgets.
/// Uses documents(type_code, number) so callers avoid N+1 lookups.
/// </summary>
public sealed class PostgresDocumentDisplayReader(IUnitOfWork uow, IDocumentTypeRegistry documentTypeRegistry)
    : IDocumentDisplayReader
{
    private sealed class DocumentRow
    {
        public Guid Id { get; init; }
        public string TypeCode { get; init; } = string.Empty;
        public string? Number { get; init; }
    }

    private sealed class TypedDisplayRow
    {
        public Guid Id { get; init; }
        public string? Display { get; init; }
    }

    public async Task<IReadOnlyDictionary<Guid, string>> ResolveAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken ct = default)
        => (await ResolveRefsAsync(ids, ct)).ToDictionary(x => x.Key, x => x.Value.Display);

    public async Task<IReadOnlyDictionary<Guid, DocumentDisplayRef>> ResolveRefsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken ct = default)
    {
        if (ids is null)
            throw new NgbArgumentRequiredException(nameof(ids));

        if (ids.Count == 0)
            return new Dictionary<Guid, DocumentDisplayRef>();

        var uniq = ids.Where(x => x != Guid.Empty).Distinct().ToArray();
        if (uniq.Length == 0)
            return new Dictionary<Guid, DocumentDisplayRef>();

        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
                           SELECT
                               id AS Id,
                               type_code AS TypeCode,
                               number AS Number
                           FROM documents
                           WHERE id = ANY(@Ids);
                           """;

        var rows = (await uow.Connection.QueryAsync<DocumentRow>(
            new CommandDefinition(sql, new { Ids = uniq }, uow.Transaction, cancellationToken: ct))).AsList();

        var typedDisplays = await LoadTypedDisplaysAsync(rows, ct);

        var map = rows
            .GroupBy(x => x.Id)
            .ToDictionary(
                x => x.Key,
                x =>
                {
                    var row = x.First();
                    var typedDisplay = typedDisplays.GetValueOrDefault(row.Id);
                    var display = !string.IsNullOrWhiteSpace(typedDisplay)
                        ? typedDisplay
                        : BuildDisplay(row);

                    return new DocumentDisplayRef(row.Id, row.TypeCode, display);
                });

        foreach (var id in uniq)
        {
            map.TryAdd(id, new DocumentDisplayRef(id, string.Empty, ShortGuid(id)));
        }

        return map;
    }

    private async Task<IReadOnlyDictionary<Guid, string>> LoadTypedDisplaysAsync(
        IReadOnlyList<DocumentRow> rows,
        CancellationToken ct)
    {
        if (rows.Count == 0)
            return new Dictionary<Guid, string>();

        var candidates = rows
            .GroupBy(x => x.TypeCode, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var meta = documentTypeRegistry.TryGet(group.Key);
                var headTable = meta?.Tables.FirstOrDefault(x => x.Kind == TableKind.Head);
                var displayColumn = headTable?.Columns
                    .FirstOrDefault(c => string.Equals(c.ColumnName, "display", StringComparison.OrdinalIgnoreCase))
                    ?.ColumnName;

                if (headTable is null || string.IsNullOrWhiteSpace(displayColumn))
                    return null;

                PostgresSqlIdentifiers.EnsureOrThrow(headTable.TableName, $"document display head table for '{group.Key}'");
                PostgresSqlIdentifiers.EnsureOrThrow(displayColumn, $"document display column for '{group.Key}'");

                return new TypedDisplayCandidate(
                    headTable.TableName,
                    displayColumn,
                    group.Select(x => x.Id).Distinct().ToArray());
            })
            .Where(x => x is not null)
            .Cast<TypedDisplayCandidate>()
            .ToList();

        if (candidates.Count == 0)
            return new Dictionary<Guid, string>();

        var sqlParts = new List<string>(candidates.Count);
        var parameters = new DynamicParameters();

        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            var idsParam = $"Ids{index}";
            parameters.Add(idsParam, candidate.Ids);

            sqlParts.Add($"""
                          SELECT
                              h.document_id AS "Id",
                              h.{Qi(candidate.DisplayColumn)} AS "Display"
                          FROM {Qi(candidate.TableName)} h
                          WHERE h.document_id = ANY(@{idsParam})
                            AND h.{Qi(candidate.DisplayColumn)} IS NOT NULL
                          """);
        }

        var sql = string.Join("\nUNION ALL\n", sqlParts) + ";";
        var cmd = new CommandDefinition(sql, parameters, transaction: uow.Transaction, cancellationToken: ct);
        var typedRows = (await uow.Connection.QueryAsync<TypedDisplayRow>(cmd)).AsList();

        return typedRows
            .GroupBy(x => x.Id)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.Display).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty);
    }

    private string BuildDisplay(DocumentRow row)
    {
        var meta = documentTypeRegistry.TryGet(row.TypeCode);
        var name = meta?.Presentation?.DisplayName;
        if (string.IsNullOrWhiteSpace(name))
            name = row.TypeCode;

        if (!string.IsNullOrWhiteSpace(row.Number))
            return $"{name} {row.Number}";

        return $"{name} {ShortGuid(row.Id)}";
    }

    private static string ShortGuid(Guid id)
    {
        var s = id.ToString("N");
        return s.Length > 8 ? s[..8] : s;
    }

    private sealed record TypedDisplayCandidate(string TableName, string DisplayColumn, Guid[] Ids);

    private static string Qi(string ident) => PostgresDocumentFilterSql.QuoteIdentifier(ident);
}
