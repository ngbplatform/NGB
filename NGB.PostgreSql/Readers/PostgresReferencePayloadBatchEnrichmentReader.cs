using System.Data;
using Dapper;
using NGB.Metadata.Base;
using NGB.Metadata.Catalogs.Storage;
using NGB.Metadata.Documents.Storage;
using NGB.Persistence.Ui;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Documents;
using NGB.PostgreSql.Internal;

namespace NGB.PostgreSql.Readers;

/// <summary>
/// Resolves all reference labels for a payload page with one database command.
/// Dynamic typed-table branches are generated only from validated metadata identifiers.
/// </summary>
public sealed class PostgresReferencePayloadBatchEnrichmentReader(
    IUnitOfWork uow,
    ICatalogTypeRegistry catalogTypes,
    IDocumentTypeRegistry documentTypes)
    : IReferencePayloadBatchEnrichmentReader
{
    private const short AccountKind = 1;
    private const short OperationalRegisterKind = 2;
    private const short CatalogKind = 3;
    private const short DocumentKind = 4;

    public async Task<ReferencePayloadBatchEnrichment> ResolveAsync(
        IReadOnlyCollection<Guid> accountIds,
        IReadOnlyCollection<Guid> operationalRegisterIds,
        IReadOnlyDictionary<string, IReadOnlyCollection<Guid>> catalogIdsByType,
        IReadOnlyCollection<Guid> documentIds,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(accountIds);
        ArgumentNullException.ThrowIfNull(operationalRegisterIds);
        ArgumentNullException.ThrowIfNull(catalogIdsByType);
        ArgumentNullException.ThrowIfNull(documentIds);

        var accounts = Normalize(accountIds);
        var registers = Normalize(operationalRegisterIds);
        var documents = Normalize(documentIds);
        var branches = new List<string>();
        var parameters = new DynamicParameters();
        var cteSql = string.Empty;

        if (accounts.Length > 0)
        {
            parameters.Add("AccountIds", accounts);
            branches.Add($"""
                          SELECT {AccountKind}::smallint AS "Kind",
                                 NULL::text AS "SourceCode",
                                 account_id AS "Id",
                                 NULL::text AS "TypeCode",
                                 NULL::text AS "Number",
                                 CASE WHEN NULLIF(BTRIM(code), '') IS NULL
                                      THEN name
                                      ELSE code || ' — ' || name
                                  END AS "Display",
                                 0 AS "Priority"
                            FROM accounting_accounts
                           WHERE account_id = ANY(@AccountIds)
                          """);
        }

        if (registers.Length > 0)
        {
            parameters.Add("OperationalRegisterIds", registers);
            branches.Add($"""
                          SELECT {OperationalRegisterKind}::smallint AS "Kind",
                                 NULL::text AS "SourceCode",
                                 register_id AS "Id",
                                 NULL::text AS "TypeCode",
                                 NULL::text AS "Number",
                                 CASE WHEN NULLIF(BTRIM(code), '') IS NULL
                                      THEN name
                                      ELSE code || ' — ' || name
                                  END AS "Display",
                                 0 AS "Priority"
                            FROM operational_registers
                           WHERE register_id = ANY(@OperationalRegisterIds)
                          """);
        }

        var catalogIndex = 0;
        foreach (var (catalogCode, rawIds) in catalogIdsByType)
        {
            var ids = Normalize(rawIds);
            if (ids.Length == 0)
                continue;

            var metadata = catalogTypes.GetRequired(catalogCode);
            var tableName = metadata.Presentation.TableName;
            var displayColumn = metadata.Presentation.DisplayColumn;

            PostgresSqlIdentifiers.EnsureOrThrow(tableName, $"catalog presentation table for '{catalogCode}'");
            PostgresSqlIdentifiers.EnsureOrThrow(displayColumn, $"catalog display column for '{catalogCode}'");

            var index = catalogIndex++;
            parameters.Add($"CatalogCode{index}", catalogCode, dbType: DbType.String);
            parameters.Add($"CatalogIds{index}", ids);
            branches.Add($"""
                          SELECT {CatalogKind}::smallint AS "Kind",
                                 @CatalogCode{index}::text AS "SourceCode",
                                 h.catalog_id AS "Id",
                                 NULL::text AS "TypeCode",
                                 NULL::text AS "Number",
                                 h.{Qi(displayColumn)}::text AS "Display",
                                 0 AS "Priority"
                            FROM {Qi(tableName)} h
                           WHERE h.catalog_id = ANY(@CatalogIds{index})
                          """);
        }

        if (documents.Length > 0)
        {
            parameters.Add("DocumentIds", documents);
            cteSql = """
                     WITH requested_documents AS MATERIALIZED (
                         SELECT id, type_code, number
                           FROM documents
                          WHERE id = ANY(@DocumentIds)
                     )
                     """;
            branches.Add($"""
                          SELECT {DocumentKind}::smallint AS "Kind",
                                 NULL::text AS "SourceCode",
                                 d.id AS "Id",
                                 d.type_code AS "TypeCode",
                                 d.number AS "Number",
                                 NULL::text AS "Display",
                                 1 AS "Priority"
                            FROM requested_documents d
                          """);

            var typedIndex = 0;
            foreach (var metadata in documentTypes.GetAll())
            {
                var head = metadata.Tables.FirstOrDefault(table => table.Kind == TableKind.Head);
                var displayColumn = head?.Columns
                    .FirstOrDefault(column => string.Equals(column.ColumnName, "display", StringComparison.OrdinalIgnoreCase))
                    ?.ColumnName;

                if (head is null || string.IsNullOrWhiteSpace(displayColumn))
                    continue;

                PostgresSqlIdentifiers.EnsureOrThrow(head.TableName, $"document display head table for '{metadata.TypeCode}'");
                PostgresSqlIdentifiers.EnsureOrThrow(displayColumn, $"document display column for '{metadata.TypeCode}'");

                parameters.Add($"DocumentType{typedIndex}", metadata.TypeCode, dbType: DbType.String);
                branches.Add($"""
                              SELECT {DocumentKind}::smallint AS "Kind",
                                     NULL::text AS "SourceCode",
                                     h.document_id AS "Id",
                                     @DocumentType{typedIndex}::text AS "TypeCode",
                                     NULL::text AS "Number",
                                     h.{Qi(displayColumn)}::text AS "Display",
                                     0 AS "Priority"
                                FROM requested_documents d
                                JOIN {Qi(head.TableName)} h ON h.document_id = d.id
                               WHERE d.type_code = @DocumentType{typedIndex}
                                 AND h.{Qi(displayColumn)} IS NOT NULL
                              """);
                typedIndex++;
            }
        }

        if (branches.Count == 0)
            return Empty(catalogIdsByType.Keys);

        await uow.EnsureConnectionOpenAsync(ct);

        var sql = cteSql + string.Join("\nUNION ALL\n", branches) + ";";
        var rows = (await uow.Connection.QueryAsync<Row>(new CommandDefinition(
            sql,
            parameters,
            transaction: uow.Transaction,
            cancellationToken: ct))).AsList();

        var accountLabels = rows.Where(row => row.Kind == AccountKind)
            .GroupBy(row => row.Id)
            .ToDictionary(group => group.Key, group => group.First().Display ?? group.Key.ToString());

        foreach (var id in accounts)
        {
            accountLabels.TryAdd(id, id.ToString());
        }

        var registerLabels = rows.Where(row => row.Kind == OperationalRegisterKind)
            .GroupBy(row => row.Id)
            .ToDictionary(group => group.Key, group => group.First().Display ?? group.Key.ToString());

        foreach (var id in registers)
        {
            registerLabels.TryAdd(id, id.ToString());
        }

        var catalogRows = rows.Where(row => row.Kind == CatalogKind && row.SourceCode is not null)
            .GroupBy(row => row.SourceCode!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyDictionary<Guid, string>)group.GroupBy(row => row.Id)
                    .ToDictionary(items => items.Key, items => items.First().Display ?? string.Empty),
                StringComparer.OrdinalIgnoreCase);
        var catalogLabels = new Dictionary<string, IReadOnlyDictionary<Guid, string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var code in catalogIdsByType.Keys)
        {
            catalogLabels[code] = catalogRows.GetValueOrDefault(code) ?? new Dictionary<Guid, string>();
        }

        var documentLabels = new Dictionary<Guid, string>();
        foreach (var group in rows.Where(row => row.Kind == DocumentKind).GroupBy(row => row.Id))
        {
            var typedDisplay = group.OrderBy(row => row.Priority)
                .Select(row => row.Display)
                .FirstOrDefault(display => !string.IsNullOrWhiteSpace(display));

            var baseRow = group.FirstOrDefault(row => row.Priority == 1);
            if (baseRow is null)
                continue;

            documentLabels[group.Key] = typedDisplay ?? BuildDocumentDisplay(baseRow);
        }

        return new ReferencePayloadBatchEnrichment(accountLabels, registerLabels, catalogLabels, documentLabels);
    }

    private string BuildDocumentDisplay(Row row)
    {
        var metadata = documentTypes.TryGet(row.TypeCode ?? string.Empty);
        var name = metadata?.Presentation?.DisplayName;
        if (string.IsNullOrWhiteSpace(name))
            name = row.TypeCode;

        return string.IsNullOrWhiteSpace(row.Number)
            ? $"{name} {row.Id.ToString("N")[..8]}"
            : $"{name} {row.Number}";
    }

    private static Guid[] Normalize(IEnumerable<Guid> ids)
        => ids.Where(static id => id != Guid.Empty).Distinct().ToArray();

    private static ReferencePayloadBatchEnrichment Empty(IEnumerable<string> catalogCodes)
        => new(
            new Dictionary<Guid, string>(),
            new Dictionary<Guid, string>(),
            catalogCodes.ToDictionary(
                code => code,
                _ => (IReadOnlyDictionary<Guid, string>)new Dictionary<Guid, string>(),
                StringComparer.OrdinalIgnoreCase),
            new Dictionary<Guid, string>());

    private static string Qi(string identifier) => PostgresDocumentFilterSql.QuoteIdentifier(identifier);

    private sealed class Row
    {
        public short Kind { get; init; }
        public string? SourceCode { get; init; }
        public Guid Id { get; init; }
        public string? TypeCode { get; init; }
        public string? Number { get; init; }
        public string? Display { get; init; }
        public int Priority { get; init; }
    }
}
