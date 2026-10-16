using Dapper;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.BusinessObjects;
using NGB.Metadata.Catalogs.Storage;
using NGB.Metadata.Documents.Storage;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Reporting;

namespace NGB.PostgreSql.Readers;

public sealed class PostgresRecordReadScopeFactory(
    IUnitOfWork uow,
    ICatalogTypeRegistry catalogs,
    IDocumentTypeRegistry documents)
    : IRecordReadScopeFactory
{
    public async Task<IAsyncDisposable> OpenAsync(
        BusinessObjectRef target,
        int maximumRows,
        int maximumBytes,
        CancellationToken ct)
    {
        if (target.Id == Guid.Empty || string.IsNullOrWhiteSpace(target.TypeCode))
            throw new ArgumentException("A canonical record is required.", nameof(target));

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRows);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);

        var isCatalog = target.Kind == BusinessObjectKind.CatalogItem;
        var tables = target.Kind switch
        {
            BusinessObjectKind.CatalogItem => catalogs.GetRequired(target.TypeCode).Tables.Select(table => table.TableName).ToArray(),
            BusinessObjectKind.Document or BusinessObjectKind.GeneralJournalEntry =>
                (documents.TryGet(target.TypeCode) ?? throw new ArgumentException("Unknown document type."))
                .Tables.Select(table => table.TableName).ToArray(),
            _ => throw new ArgumentException("Unsupported record kind.")
        };

        if (tables.Length > 64)
            throw new RecordReadLimitExceededException();

        var session = new PostgresReportReadSession(uow);
        await session.BeginAsync(ct);
        try
        {
            var baseTable = isCatalog ? "catalogs" : "documents";
            var typeColumn = isCatalog ? "catalog_code" : "type_code";
            var parentColumn = isCatalog ? "catalog_id" : "document_id";
            var queries = new List<string>
            {
                SizeQuery(baseTable, "id", $" AND p.{Quote(typeColumn)} = @typeCode")
            };
            queries.AddRange(tables.Distinct(StringComparer.Ordinal).Select(table => SizeQuery(table, parentColumn, string.Empty)));
            var sizes = await uow.Connection.QueryAsync<RecordSize>(new CommandDefinition(
                string.Join("\nUNION ALL\n", queries),
                // Probe one extra row without overflowing when the caller uses int.MaxValue.
                new { id = target.Id, typeCode = target.TypeCode, take = (long)maximumRows + 1 },
                transaction: uow.Transaction,
                cancellationToken: ct));

            if (sizes.Sum(size => size.Rows) > maximumRows || sizes.Sum(size => size.Bytes) > maximumBytes)
                throw new RecordReadLimitExceededException();

            return new ReadScope(session);
        }
        catch
        {
            await session.EndAsync(CancellationToken.None);
            throw;
        }
    }

    private static string SizeQuery(string table, string parentColumn, string predicate)
        => $"""
            SELECT count(*) AS "Rows", COALESCE(sum(b."Bytes"), 0)::bigint AS "Bytes"
              FROM (
                  SELECT octet_length(row_to_json(p)::text)::bigint AS "Bytes"
                    FROM {Quote(table)} p
                   WHERE p.{Quote(parentColumn)} = @id{predicate}
                   LIMIT @take
              ) b
            """;

    private static string Quote(string identifier)
        => '"' + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';

    private sealed record RecordSize(long Rows, long Bytes);

    private sealed class ReadScope(PostgresReportReadSession session) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await session.EndAsync(CancellationToken.None);
    }
}
