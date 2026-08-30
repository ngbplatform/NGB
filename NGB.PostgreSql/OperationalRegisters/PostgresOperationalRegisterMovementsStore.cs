using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using NGB.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.OperationalRegisters.Exceptions;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Internal;
using NGB.PostgreSql.Schema;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PostgreSql.OperationalRegisters;

public sealed class PostgresOperationalRegisterMovementsStore(
    IUnitOfWork uow,
    IOperationalRegisterRepository registersRepo,
    IOperationalRegisterResourceRepository resourcesRepo,
    OperationalRegisterMetadataCache? metadataCache = null,
    PostgresRelationShapeCache? relationShapeCache = null)
    : IOperationalRegisterMovementsStore
{
    private readonly OperationalRegisterMetadataCache _metadataCache = metadataCache
        ?? new OperationalRegisterMetadataCache(TimeProvider.System);
    private readonly PostgresRelationShapeCache _relationShapeCache = relationShapeCache
        ?? new PostgresRelationShapeCache(TimeProvider.System);
    private readonly ConcurrentDictionary<Guid, ScopedMetadata> _scopedMetadata = new();
    private readonly ConcurrentDictionary<Guid, SchemaReadiness> _schemasReadyForWrite = new();
    private readonly ConcurrentDictionary<Guid, SchemaReadiness> _hasMovementsEnsured = new();

    public async Task EnsureSchemaAsync(Guid registerId, CancellationToken ct = default)
    {
        await uow.EnsureConnectionOpenAsync(ct);

        await using var schemaLock = await PostgresOperationalRegisterSchemaLock.AcquireAsync(uow, registerId, ct);

        // Explicit schema ensure follows metadata administration. Refresh even mutable
        // transaction-scoped metadata so newly replaced resources become physical columns.
        var context = await RefreshMetadataAsync(registerId, ct);
        await EnsureSchemaCoreAsync(registerId, context, ct);
    }

    private async Task EnsureSchemaCoreAsync(
        Guid registerId,
        OperationalRegisterMetadataContext context,
        CancellationToken ct)
    {
        var table = context.MovementsTable;
        var resources = context.Resources;

        // Execute the complete idempotent schema contract in one roundtrip while
        // the per-register advisory lock is held.
        var ddl = new StringBuilder($"""
CREATE TABLE IF NOT EXISTS {table}(
    movement_id BIGSERIAL PRIMARY KEY,
    document_id UUID NOT NULL,
    occurred_at_utc TIMESTAMPTZ NOT NULL,
    -- IMPORTANT: period_month must be computed in UTC, independent of the PostgreSQL session TimeZone.
    period_month DATE GENERATED ALWAYS AS (date_trunc('month', (occurred_at_utc AT TIME ZONE 'UTC'))::date) STORED,
    dimension_set_id UUID NOT NULL DEFAULT '{Guid.Empty}',
    is_storno BOOLEAN NOT NULL DEFAULT FALSE,
    FOREIGN KEY (dimension_set_id) REFERENCES platform_dimension_sets(dimension_set_id)
);
""");

        // Resource columns (NUMERIC(28,8) NOT NULL DEFAULT 0).
        foreach (var resource in resources)
        {
            ddl.AppendLine($"ALTER TABLE {table} ADD COLUMN IF NOT EXISTS {resource.ColumnCode} NUMERIC(28,8) NOT NULL DEFAULT 0;");
        }

        // Append-only guard.
        var trgAppendOnly = Trg(table, "append_only");
        ddl.AppendLine(PostgresAppendOnlyGuardSql.BuildUpdateDeleteForbiddenTriggerSql(table, trgAppendOnly));

        // Basic indexes.
        // NOTE: avoid string literals inside interpolation (would break C# parsing).
        var ixDoc = Ix(table, "doc");
        var ixMonth = Ix(table, "month");
        var ixMonthDim = Ix(table, "month_dim");

        // Large-scale / paging indexes.
        // - month_move: supports paging by movement_id within a month (GetByMonth without dimension filter).
        // - month_dim_move: supports paging by movement_id within a (month, dimension_set_id) slice.
        // - doc_month_nostorno: supports distinct months lookup per document and speeds up storno appends
        //   (we only storno non-storno rows).
        var ixMonthMove = Ix(table, "month_move");
        var ixMonthDimMove = Ix(table, "month_dim_move");
        var ixDimMonthOccurred = Ix(table, "dim_month_occurred");
        var ixOccurredMove = Ix(table, "occurred_move");
        var ixDocMonthNoStorno = Ix(table, "doc_month_nostorno");

        ddl.AppendLine($"CREATE INDEX IF NOT EXISTS {ixDoc} ON {table}(document_id);");
        ddl.AppendLine($"CREATE INDEX IF NOT EXISTS {ixMonth} ON {table}(period_month);");
        ddl.AppendLine($"CREATE INDEX IF NOT EXISTS {ixMonthDim} ON {table}(period_month, dimension_set_id);");
        ddl.AppendLine($"CREATE INDEX IF NOT EXISTS {ixMonthMove} ON {table}(period_month, movement_id);");
        ddl.AppendLine($"CREATE INDEX IF NOT EXISTS {ixMonthDimMove} ON {table}(period_month, dimension_set_id, movement_id);");
        ddl.AppendLine($"CREATE INDEX IF NOT EXISTS {ixDimMonthOccurred} ON {table}(dimension_set_id, period_month, occurred_at_utc);");
        ddl.AppendLine($"CREATE INDEX IF NOT EXISTS {ixOccurredMove} ON {table}(occurred_at_utc, movement_id);");
        ddl.AppendLine($"CREATE INDEX IF NOT EXISTS {ixDocMonthNoStorno} ON {table}(document_id, period_month) WHERE is_storno = FALSE;");

        await uow.Connection.ExecuteAsync(new CommandDefinition(
            ddl.ToString(),
            transaction: uow.Transaction,
            cancellationToken: ct));

        MarkSchemaReady(registerId, table, resources);
    }

    public async Task EnsureReadyForWriteAsync(Guid registerId, CancellationToken ct = default)
    {
        if (IsReadyInCurrentTransaction(registerId))
            return;

        var context = await GetMetadataAsync(registerId, ct);
        var register = context.Register;

        if (register.HasMovements)
        {
            var requiredColumns = context.Resources
                .Select(resource => resource.ColumnCode)
                .Prepend("movement_id")
                .ToArray();

            var ready = uow.Transaction is null
                ? await _relationShapeCache.IsVerifiedAsync(
                    context.MovementsTable,
                    ShapeFingerprint(context.Resources),
                    probeCt => PostgresTableColumnReadiness.HasRequiredColumnsAsync(
                        uow,
                        context.MovementsTable,
                        requiredColumns,
                        probeCt),
                    ct)
                : await PostgresTableColumnReadiness.HasRequiredColumnsAsync(
                    uow,
                    context.MovementsTable,
                    requiredColumns,
                    ct);

            if (ready)
            {
                _schemasReadyForWrite[registerId] = new SchemaReadiness(uow.Transaction);
                return;
            }
        }

        await uow.EnsureConnectionOpenAsync(ct);
        await using var schemaLock = await PostgresOperationalRegisterSchemaLock.AcquireAsync(uow, registerId, ct);
        await EnsureSchemaCoreAsync(registerId, context, ct);
    }

    public async Task AppendAsync(Guid registerId, IReadOnlyList<OperationalRegisterMovement> movements, CancellationToken ct = default)
    {
        if (movements is null)
            throw new NgbArgumentRequiredException(nameof(movements));

        if (movements.Count == 0)
            return;

        // Reject invalid batches before opening a connection or loading dynamic metadata.
        for (var i = 0; i < movements.Count; i++)
        {
            var movement = movements[i];
            if (movement.DocumentId == Guid.Empty)
                throw new NgbArgumentInvalidException($"movements[{i}].DocumentId", "DocumentId must be non-empty.");

            movement.OccurredAtUtc.EnsureUtc(nameof(movement.OccurredAtUtc));
        }

        await uow.EnsureConnectionOpenAsync(ct);
        uow.EnsureActiveTransaction();

        var context = await GetMetadataAsync(registerId, ct);
        var table = context.MovementsTable;
        var resources = context.Resources;

        ValidateResourceKeys(registerId, resources, movements);

        // Flip has_movements inside the same transaction as the append.
        // This is used by DB-level guards (e.g. resources immutability) and must be rollback-safe.
        await EnsureHasMovementsFlagAsync(context, ct);

        var docIds = movements.Select(m => m.DocumentId).ToArray();
        var occurred = movements.Select(m => m.OccurredAtUtc).ToArray();
        var dimSetIds = movements.Select(m => m.DimensionSetId).ToArray();
        var isStorno = new bool[movements.Count];

        var resourceArrays = BuildResourceArrays(resources, movements);

        var cols = new List<string> { "document_id", "occurred_at_utc", "dimension_set_id", "is_storno" };
        cols.AddRange(resources.Select(r => r.ColumnCode));

        var unnestCols = new List<string> { "document_id", "occurred_at_utc", "dimension_set_id", "is_storno" };
        unnestCols.AddRange(resources.Select(r => r.ColumnCode));

        var unnestArgs = new List<string> { "@DocumentIds::uuid[]", "@OccurredAtUtc::timestamptz[]", "@DimensionSetIds::uuid[]", "@IsStorno::boolean[]" };
        unnestArgs.AddRange(resources.Select(r => $"@{Param(r.ColumnCode)}::numeric[]"));

        var selectCols = new List<string> { "x.document_id", "x.occurred_at_utc", "x.dimension_set_id", "x.is_storno" };
        selectCols.AddRange(resources.Select(r => $"x.{r.ColumnCode}"));

        var sql = $"""
INSERT INTO {table} ({string.Join(", ", cols)})
SELECT {string.Join(", ", selectCols)}
FROM UNNEST({string.Join(", ", unnestArgs)}) AS x({string.Join(", ", unnestCols)});
""";

        var parameters = new DynamicParameters();
        parameters.Add("DocumentIds", docIds);
        parameters.Add("OccurredAtUtc", occurred);
        parameters.Add("DimensionSetIds", dimSetIds);
        parameters.Add("IsStorno", isStorno);
        foreach (var (paramName, values) in resourceArrays)
            parameters.Add(paramName, values);

        await uow.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            parameters,
            transaction: uow.Transaction,
            cancellationToken: ct));
    }

    public async Task AppendStornoByDocumentAsync(Guid registerId, Guid documentId, CancellationToken ct = default)
    {
        await uow.EnsureConnectionOpenAsync(ct);
        uow.EnsureActiveTransaction();

        var context = await GetMetadataAsync(registerId, ct);
        var table = context.MovementsTable;
        var resources = context.Resources;

        var resourceCols = resources.Length == 0
            ? string.Empty
            : ", " + string.Join(", ", resources.Select(r => r.ColumnCode));

        // Storno semantics:
        // - The net effect of a document is computed as SUM(non-storno) - SUM(storno).
        // - To cancel the *current* net effect (after any number of Post/Repost cycles),
        //   we must append an opposite-sign row for EACH historical movement row.
        //   This is achieved by toggling is_storno.
        var sql = $"""
INSERT INTO {table} (document_id, occurred_at_utc, dimension_set_id, is_storno{resourceCols})
SELECT document_id, occurred_at_utc, dimension_set_id, NOT is_storno{resourceCols}
FROM {table}
WHERE document_id = @DocumentId;
""";

        // Storno append also counts as a movement (if someone somehow calls it first).
        await EnsureHasMovementsFlagAsync(context, ct);

        await uow.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { DocumentId = documentId },
            transaction: uow.Transaction,
            cancellationToken: ct));
    }

    private Task<OperationalRegisterMetadataContext> GetMetadataAsync(Guid registerId, CancellationToken ct)
    {
        if (_scopedMetadata.TryGetValue(registerId, out var cached)
            && (cached.Context.Register.HasMovements || ReferenceEquals(cached.Transaction, uow.Transaction)))
        {
            return Task.FromResult(cached.Context);
        }

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
        _scopedMetadata[registerId] = new ScopedMetadata(context, uow.Transaction);
        return context;
    }

    private async Task<OperationalRegisterMetadataContext> RefreshMetadataAsync(
        Guid registerId,
        CancellationToken ct)
    {
        var context = await LoadMetadataAsync(registerId, ct);
        _metadataCache.Remember(context);
        _scopedMetadata[registerId] = new ScopedMetadata(context, uow.Transaction);
        return context;
    }

    private async Task<OperationalRegisterMetadataContext> LoadMetadataAsync(Guid registerId, CancellationToken ct)
    {
        var register = await registersRepo.GetByIdAsync(registerId, ct)
            ?? throw new OperationalRegisterNotFoundException(registerId);
        var table = OperationalRegisterNaming.MovementsTable(register.TableCode);

        OperationalRegisterSqlIdentifiers.EnsureOrThrow(table, "opreg movements table name");

        var resources = (await resourcesRepo.GetByRegisterIdAsync(registerId, ct))
            .OrderBy(resource => resource.Ordinal)
            .ToArray();

        foreach (var resource in resources)
        {
            OperationalRegisterSqlIdentifiers.EnsureOrThrow(resource.ColumnCode, "opreg resource column_code");
        }

        return new OperationalRegisterMetadataContext(register, resources, table);
    }

    private async Task EnsureHasMovementsFlagAsync(OperationalRegisterMetadataContext context, CancellationToken ct)
    {
        if (context.Register.HasMovements || IsCurrent(_hasMovementsEnsured, context.Register.RegisterId))
            return;

        await uow.Connection.ExecuteAsync(new CommandDefinition(
            "UPDATE operational_registers SET has_movements = TRUE, updated_at_utc = NOW() WHERE register_id = @RegisterId AND has_movements = FALSE;",
            new { RegisterId = context.Register.RegisterId },
            transaction: uow.Transaction,
            cancellationToken: ct));

        _hasMovementsEnsured[context.Register.RegisterId] = new SchemaReadiness(uow.Transaction);
    }

    private void MarkSchemaReady(Guid registerId, string table, OperationalRegisterResource[] resources)
    {
        _schemasReadyForWrite[registerId] = new SchemaReadiness(uow.Transaction);
        if (uow.Transaction is null)
            _relationShapeCache.MarkVerified(table, ShapeFingerprint(resources));
    }

    private bool IsReadyInCurrentTransaction(Guid registerId) => IsCurrent(_schemasReadyForWrite, registerId);

    private bool IsCurrent(
        ConcurrentDictionary<Guid, SchemaReadiness> readinessByRegister,
        Guid registerId)
        => readinessByRegister.TryGetValue(registerId, out var readiness)
           && ReferenceEquals(readiness.Transaction, uow.Transaction);

    private static string ShapeFingerprint(IEnumerable<OperationalRegisterResource> resources)
        => string.Join('|', resources.Select(static resource => resource.ColumnCode));

    private sealed record SchemaReadiness(object? Transaction);
    private sealed record ScopedMetadata(OperationalRegisterMetadataContext Context, object? Transaction);

    internal static List<(string ParamName, decimal[] Values)> BuildResourceArrays(
        OperationalRegisterResource[] resources,
        IReadOnlyList<OperationalRegisterMovement> movements)
    {
        var result = new List<(string ParamName, decimal[] Values)>(resources.Length);

        foreach (var r in resources)
        {
            var arr = new decimal[movements.Count];

            for (var i = 0; i < movements.Count; i++)
            {
                var m = movements[i];

                if (m.Resources.TryGetValue(r.ColumnCode, out var v))
                    arr[i] = v;
            }

            result.Add((Param(r.ColumnCode), arr));
        }

        return result;
    }

    internal static void ValidateResourceKeys(
        Guid registerId,
        OperationalRegisterResource[] resources,
        IReadOnlyList<OperationalRegisterMovement> movements)
    {
        if (movements.Count == 0)
            return;

        if (resources.Length == 0)
        {
            for (var i = 0; i < movements.Count; i++)
            {
                var map = movements[i].Resources
                    ?? throw new NgbArgumentInvalidException($"movements[{i}].Resources", "Resources must be non-null.");

                if (map.Count == 0)
                    continue;

                var unknownKeys = map.Keys.Take(10).ToArray();
                var first = unknownKeys[0];
                var reason = $"Unknown resource column '{first}'. Ensure it exists in operational_register_resources.";

                throw new OperationalRegisterResourcesValidationException(
                    registerId,
                    reason: reason,
                    details: new Dictionary<string, object?>
                    {
                        ["movementIndex"] = i,
                        ["unknownKey"] = first,
                        ["unknownKeys"] = unknownKeys
                    });
            }

            return;
        }

        var allowed = new HashSet<string>(resources.Select(r => r.ColumnCode), StringComparer.Ordinal);

        for (var i = 0; i < movements.Count; i++)
        {
            var map = movements[i].Resources
                ?? throw new NgbArgumentInvalidException($"movements[{i}].Resources", "Resources must be non-null.");

            foreach (var k in map.Keys)
            {
                if (!allowed.Contains(k))
                {
                    var reason = $"Unknown resource column '{k}'. Ensure it exists in operational_register_resources.";

                    throw new OperationalRegisterResourcesValidationException(
                        registerId,
                        reason: reason,
                        details: new Dictionary<string, object?>
                        {
                            ["movementIndex"] = i,
                            ["unknownKey"] = k
                        });
                }
            }
        }
    }

    private static string Param(string columnCode) => "p_" + columnCode;

    private static string Ix(string table, string purpose)
        => "ix_opreg_" + purpose + "_" + Hash8(table + "|" + purpose);

    private static string Trg(string table, string purpose)
        => "trg_opreg_" + purpose + "_" + Hash8(table + "|" + purpose);

    private static string Hash8(string s)
        => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant()[..8];
}
