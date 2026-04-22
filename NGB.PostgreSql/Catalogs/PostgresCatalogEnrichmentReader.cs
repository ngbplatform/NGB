using Dapper;
using NGB.Core.Catalogs.Exceptions;
using NGB.Metadata.Catalogs.Storage;
using NGB.Persistence.Catalogs.Enrichment;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Documents;
using NGB.PostgreSql.Internal;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.Catalogs;

public sealed class PostgresCatalogEnrichmentReader(IUnitOfWork uow, ICatalogTypeRegistry registry)
    : ICatalogEnrichmentReader
{
    public async Task<IReadOnlyDictionary<Guid, string>> ResolveAsync(
        string catalogCode,
        IReadOnlyList<Guid> ids,
        CancellationToken ct = default)
    {
        var batch = await ResolveManyAsync(
            new Dictionary<string, IReadOnlyCollection<Guid>>(StringComparer.OrdinalIgnoreCase)
            {
                [catalogCode] = ids
            },
            ct);

        return batch.TryGetValue(catalogCode, out var resolved)
            ? resolved
            : new Dictionary<Guid, string>();
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyDictionary<Guid, string>>> ResolveManyAsync(
        IReadOnlyDictionary<string, IReadOnlyCollection<Guid>> idsByCatalogCode,
        CancellationToken ct = default)
    {
        if (idsByCatalogCode.Count == 0 || idsByCatalogCode.All(x => x.Value.Count == 0))
            return new Dictionary<string, IReadOnlyDictionary<Guid, string>>(StringComparer.OrdinalIgnoreCase);

        var entries = new List<CatalogBatchEntry>();

        foreach (var (catalogCode, rawIds) in idsByCatalogCode)
        {
            var ids = rawIds
                .Where(x => x != Guid.Empty)
                .Distinct()
                .ToArray();

            if (ids.Length == 0)
                continue;

            var meta = registry.GetRequired(catalogCode);
            var tableName = meta.Presentation.TableName;
            var displayColumn = meta.Presentation.DisplayColumn;

            ValidateIdentifiersOrThrow(catalogCode, tableName, displayColumn);

            entries.Add(new CatalogBatchEntry(catalogCode, tableName, displayColumn, ids));
        }

        if (entries.Count == 0)
            return new Dictionary<string, IReadOnlyDictionary<Guid, string>>(StringComparer.OrdinalIgnoreCase);

        await uow.EnsureConnectionOpenAsync(ct);

        var sqlParts = new List<string>(entries.Count);
        var parameters = new DynamicParameters();

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var catalogCodeParam = $"CatalogCode{index}";
            var idsParam = $"Ids{index}";

            parameters.Add(catalogCodeParam, entry.CatalogCode);
            parameters.Add(idsParam, entry.Ids);

            sqlParts.Add($"""
                          SELECT
                              @{catalogCodeParam} AS "CatalogCode",
                              c.catalog_id AS "Id",
                              c.{Qi(entry.DisplayColumn)} AS "Display"
                          FROM {Qi(entry.TableName)} c
                          WHERE c.catalog_id = ANY(@{idsParam})
                          """);
        }

        var sql = string.Join("\nUNION ALL\n", sqlParts) + ";";
        var cmd = new CommandDefinition(sql, parameters, transaction: uow.Transaction, cancellationToken: ct);
        var rows = (await uow.Connection.QueryAsync<Row>(cmd)).AsList();
        var rowsByCatalog = rows
            .GroupBy(x => x.CatalogCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyDictionary<Guid, string>)g
                    .GroupBy(x => x.Id)
                    .ToDictionary(x => x.Key, x => x.First().Display ?? string.Empty),
                StringComparer.OrdinalIgnoreCase);

        var result = new Dictionary<string, IReadOnlyDictionary<Guid, string>>(entries.Count, StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            result[entry.CatalogCode] = rowsByCatalog.GetValueOrDefault(entry.CatalogCode)
                ?? new Dictionary<Guid, string>();
        }

        return result;
    }

    private static void ValidateIdentifiersOrThrow(string catalogCode, string tableName, string displayColumn)
    {
        try
        {
            PostgresSqlIdentifiers.EnsureOrThrow(tableName, $"catalog presentation table for '{catalogCode}'");
            PostgresSqlIdentifiers.EnsureOrThrow(displayColumn, $"catalog presentation display column for '{catalogCode}'");
        }
        catch (NgbConfigurationViolationException)
        {
            throw new CatalogPresentationMetadataUnsafeIdentifierException(catalogCode, tableName, displayColumn);
        }
    }

    private sealed class Row
    {
        public string CatalogCode { get; init; } = string.Empty;
        public Guid Id { get; init; }
        public string? Display { get; init; }
    }

    private sealed record CatalogBatchEntry(
        string CatalogCode,
        string TableName,
        string DisplayColumn,
        Guid[] Ids);

    private static string Qi(string ident) => PostgresDocumentFilterSql.QuoteIdentifier(ident);
}
