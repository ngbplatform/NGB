using System.Collections.Concurrent;
using Dapper;
using NGB.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.OperationalRegisters.Exceptions;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Internal;
using NGB.PostgreSql.Schema;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.OperationalRegisters;

/// <summary>
/// Reader for per-register movements tables (opreg_*__movements).
/// Works both inside and outside a transaction; if the table has not been created yet, returns empty results.
///
/// Notes:
/// - The physical schema is defined by register resources (operational_register_resources).
/// - Returned movements include the <c>IsStorno</c> flag and typed resource values.
/// </summary>
public sealed class PostgresOperationalRegisterMovementsReader(
    IUnitOfWork uow,
    IOperationalRegisterRepository registers,
    IOperationalRegisterResourceRepository resources,
    OperationalRegisterMetadataCache? metadataCache = null,
    PostgresRelationPresenceCache? relationPresenceCache = null)
    : IOperationalRegisterMovementsReader
{
    private readonly OperationalRegisterMetadataCache _metadataCache = metadataCache
        ?? new OperationalRegisterMetadataCache(TimeProvider.System);
    private readonly PostgresRelationPresenceCache _relationPresenceCache = relationPresenceCache
        ?? new PostgresRelationPresenceCache(TimeProvider.System);
    private readonly ConcurrentDictionary<Guid, OperationalRegisterMetadataContext> _localMetadata = new();
    // IMPORTANT: identifiers are used unquoted in dynamic SQL; Postgres requires unquoted identifiers
    // to start with a letter or underscore.

    public async Task<IReadOnlyList<OperationalRegisterMovementRead>> GetByMonthAsync(
        Guid registerId,
        DateOnly periodMonth,
        Guid? dimensionSetId = null,
        long? afterMovementId = null,
        int limit = 1000,
        CancellationToken ct = default)
    {
        if (registerId == Guid.Empty)
            throw new NgbArgumentRequiredException(nameof(registerId));

        if (limit <= 0)
            throw new NgbArgumentOutOfRangeException(nameof(limit), limit, "Limit must be greater than 0.");

        await uow.EnsureConnectionOpenAsync(ct);

        var context = await GetMetadataAsync(registerId, ct);
        var tableName = context.MovementsTable;
        var resourceColumns = context.Resources.Select(static resource => resource.ColumnCode).ToArray();
        
        if (!await TableExistsAsync(tableName, ct))
            return [];

        periodMonth = OperationalRegisterPeriod.MonthStart(periodMonth);

        var resourcesSelect = resourceColumns.Length == 0
            ? string.Empty
            : ", " + string.Join(", ", resourceColumns.Select(c => $"{c} AS \"{c}\""));

        var sql = $"""
                   SELECT
                       movement_id       AS "MovementId",
                       document_id       AS "DocumentId",
                       occurred_at_utc    AS "OccurredAtUtc",
                       dimension_set_id   AS "DimensionSetId",
                       is_storno          AS "IsStorno"{resourcesSelect}
                   FROM {tableName}
                   WHERE period_month = @PeriodMonth
                     AND (@DimensionSetId IS NULL OR dimension_set_id = @DimensionSetId)
                     AND (@AfterMovementId IS NULL OR movement_id > @AfterMovementId)
                   ORDER BY movement_id
                   LIMIT @Limit;
                   """;

        var cmd = new CommandDefinition(
            sql,
            new
            {
                PeriodMonth = periodMonth,
                DimensionSetId = dimensionSetId,
                AfterMovementId = afterMovementId,
                Limit = limit
            },
            transaction: uow.Transaction,
            cancellationToken: ct);

        var rows = await uow.Connection.QueryAsync(cmd);

        var result = new List<OperationalRegisterMovementRead>();
        foreach (var row in rows)
        {
            var d = (IDictionary<string, object?>)row;

            var movementId = Convert.ToInt64(d["MovementId"]!);
            var documentId = (Guid)d["DocumentId"]!;
            var occurredAtUtc = (DateTime)d["OccurredAtUtc"]!;
            var dimSetId = (Guid)d["DimensionSetId"]!;
            var isStorno = (bool)d["IsStorno"]!;

            var values = new Dictionary<string, decimal>(StringComparer.Ordinal);
            foreach (var col in resourceColumns)
            {
                var v = d.TryGetValue(col, out var obj) ? obj : null;

                if (v is null or DBNull)
                {
                    values[col] = 0m;
                    continue;
                }

                values[col] = Convert.ToDecimal(v);
            }

            result.Add(new OperationalRegisterMovementRead(movementId, documentId, occurredAtUtc, dimSetId, isStorno, values));
        }

        return result;
    }

    public async Task<IReadOnlyList<DateOnly>> GetDistinctMonthsByDocumentAsync(
        Guid registerId,
        Guid documentId,
        CancellationToken ct = default)
    {
        if (registerId == Guid.Empty)
            throw new NgbArgumentRequiredException(nameof(registerId));

        if (documentId == Guid.Empty)
            throw new NgbArgumentRequiredException(nameof(documentId));

        await uow.EnsureConnectionOpenAsync(ct);

        var tableName = (await GetMetadataAsync(registerId, ct)).MovementsTable;
        
        if (!await TableExistsAsync(tableName, ct))
            return [];

        var sql = $"""
                   SELECT DISTINCT period_month AS "Month"
                   FROM {tableName}
                   WHERE document_id = @DocumentId AND is_storno = FALSE
                   ORDER BY period_month;
                   """;

        var cmd = new CommandDefinition(
            sql,
            new { DocumentId = documentId },
            transaction: uow.Transaction,
            cancellationToken: ct);

        var months = await uow.Connection.QueryAsync<DateOnly>(cmd);
        return months.AsList();
    }

    private Task<OperationalRegisterMetadataContext> GetMetadataAsync(Guid registerId, CancellationToken ct)
    {
        if (_localMetadata.TryGetValue(registerId, out var cached))
            return Task.FromResult(cached);

        return LoadAndRememberMetadataAsync(registerId, ct);
    }

    private async Task<OperationalRegisterMetadataContext> LoadAndRememberMetadataAsync(
        Guid registerId,
        CancellationToken ct)
    {
        var context = await _metadataCache.GetOrCreateAsync(
            registerId,
            loadCt => LoadMetadataAsync(registerId, loadCt),
            ct);
        _localMetadata[registerId] = context;

        return context;
    }

    private async Task<OperationalRegisterMetadataContext> LoadMetadataAsync(Guid registerId, CancellationToken ct)
    {
        var register = await registers.GetByIdAsync(registerId, ct)
            ?? throw new OperationalRegisterNotFoundException(registerId);
        var table = OperationalRegisterNaming.MovementsTable(register.TableCode);

        OperationalRegisterSqlIdentifiers.EnsureOrThrow(table, "opreg movements table name");

        var resourceDefinitions = (await resources.GetByRegisterIdAsync(registerId, ct))
            .OrderBy(static resource => resource.Ordinal)
            .ToArray();

        foreach (var resource in resourceDefinitions)
        {
            OperationalRegisterSqlIdentifiers.EnsureOrThrow(resource.ColumnCode, "opreg resource column_code");
        }

        return new OperationalRegisterMetadataContext(register, resourceDefinitions, table);
    }

    private Task<bool> TableExistsAsync(string tableName, CancellationToken ct)
        => _relationPresenceCache.ExistsAsync(
            tableName,
            probeCt => PostgresTableExistence.ExistsAsync(uow, tableName, probeCt),
            ct);
}
