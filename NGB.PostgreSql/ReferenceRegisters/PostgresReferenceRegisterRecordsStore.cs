using System.Text;
using Dapper;
using NGB.Metadata.Base;
using NGB.Persistence.ReferenceRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.UnitOfWork;
using NGB.PostgreSql.Internal;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.ReferenceRegisters.Exceptions;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PostgreSql.ReferenceRegisters;

/// <summary>
/// PostgreSQL implementation of per-reference-register physical records tables.
///
/// Table name:
///   refreg_&lt;table_code&gt;__records
///
/// Base columns:
/// - record_id BIGSERIAL PK
/// - dimension_set_id UUID NOT NULL FK platform_dimension_sets (Guid.Empty = "empty")
/// - period_utc TIMESTAMPTZ (NULL for NonPeriodic)
/// - period_bucket_utc TIMESTAMPTZ (NULL for NonPeriodic)
/// - recorder_document_id UUID (NOT NULL for SubordinateToRecorder)
/// - recorded_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()
/// - is_deleted BOOLEAN NOT NULL DEFAULT false
///
/// All per-register record tables are append-only (UPDATE/DELETE forbidden via shared guard function).
/// </summary>
public sealed class PostgresReferenceRegisterRecordsStore(
    IUnitOfWork uow,
    IReferenceRegisterRepository registers,
    IReferenceRegisterFieldRepository fieldsRepo)
    : IReferenceRegisterRecordsStore, IReferenceRegisterRecorderTombstoneWriter
{
    public async Task EnsureSchemaAsync(Guid registerId, CancellationToken ct = default)
    {
        registerId.EnsureNonEmpty(nameof(registerId));
        await uow.EnsureConnectionOpenAsync(ct);

        await using var _ = await PostgresReferenceRegisterSchemaLock.AcquireAsync(uow, registerId, ct);

        var reg = await registers.GetByIdAsync(registerId, ct)
                  ?? throw new ReferenceRegisterNotFoundException(registerId);

        var table = ReferenceRegisterNaming.RecordsTable(reg.TableCode);
        ReferenceRegisterSqlIdentifiers.EnsureOrThrow(table, "records table");

        // Create base table.
        await EnsureRecordsTableAsync(table, ct);

        // Apply per-register constraints (periodicity / recorder mode) as drift-repair.
        await EnsureBaseColumnConstraintsAsync(table, reg, ct);

        // Ensure field columns.
        var fields = await fieldsRepo.GetByRegisterIdAsync(registerId, ct);
        await EnsureFieldColumnsAsync(table, fields, reg.HasRecords, ct);

        // Ensure append-only guards + indexes.
        await PostgresAppendOnlyGuardSql.EnsureUpdateDeleteForbiddenTriggerAsync(uow, table, Trg("trg_refreg_append_only_", table), ct);
        await EnsureIndexesAsync(table, reg, ct);
    }

    public async Task AppendAsync(
        Guid registerId,
        IReadOnlyList<ReferenceRegisterRecordWrite> records,
        CancellationToken ct = default)
    {
        registerId.EnsureNonEmpty(nameof(registerId));
        if (records is null)
            throw new NgbArgumentRequiredException(nameof(records));

        if (records.Count == 0)
            return;

        await uow.EnsureOpenForTransactionAsync(ct);

        // Ensure physical schema before inserting.
        await EnsureSchemaAsync(registerId, ct);

        var reg = await registers.GetByIdAsync(registerId, ct)
                  ?? throw new ReferenceRegisterNotFoundException(registerId);

        var table = ReferenceRegisterNaming.RecordsTable(reg.TableCode);

        var fields = await fieldsRepo.GetByRegisterIdAsync(registerId, ct);
        var fieldByCodeNorm = fields.ToDictionary(x => x.CodeNorm, x => x, StringComparer.Ordinal);

        ValidateRecords(registerId, reg, records, fieldByCodeNorm);

        // Mark registry as having records (enables metadata guards).
        // Safe idempotent update.
        {
            const string sql = "UPDATE reference_registers SET has_records = TRUE, updated_at_utc = NOW() WHERE register_id = @Id AND has_records = FALSE;";
            var cmd = new CommandDefinition(sql, new { Id = registerId }, transaction: uow.Transaction, cancellationToken: ct);
            await uow.Connection.ExecuteAsync(cmd);
        }

        var insertPrefix = BuildInsertPrefix(table, fields);

        var paramsPerRow = 5 + fields.Count;
        // Keep batches small to avoid huge SQL strings.
        const int maxParamsPerBatch = 8000;
        var batchSize = Math.Clamp(maxParamsPerBatch / Math.Max(1, paramsPerRow), 1, 500);

        for (var offset = 0; offset < records.Count; offset += batchSize)
        {
            var take = Math.Min(batchSize, records.Count - offset);

            var sql = new StringBuilder(capacity: 256 + take * 64);
            sql.Append(insertPrefix);

            var p = new DynamicParameters();

            for (var i = 0; i < take; i++)
            {
                if (i > 0)
                    sql.Append(",\n");

                var r = records[offset + i];
                var bucketUtc = ReferenceRegisterPeriodBucket.ComputeUtc(r.PeriodUtc, reg.Periodicity);

                p.Add(P("DimensionSetId", i), r.DimensionSetId == Guid.Empty ? Guid.Empty : r.DimensionSetId);
                p.Add(P("PeriodUtc", i), r.PeriodUtc);
                p.Add(P("PeriodBucketUtc", i), bucketUtc);
                p.Add(P("RecorderDocumentId", i), r.RecorderDocumentId);
                p.Add(P("IsDeleted", i), r.IsDeleted);

                foreach (var f in fields)
                {
                    var paramName = P(Param(f.ColumnCode), i);
                    r.Values.TryGetValue(f.CodeNorm, out var v);

                    if (v is null)
                    {
                        if (!f.IsNullable)
                            throw new ReferenceRegisterRecordsValidationException(registerId,
                                reason: "missing_not_null_field", details: new { fieldCodeNorm = f.CodeNorm });
                    }
                    else
                    {
                        if (f.ColumnType == ColumnType.DateTimeUtc && v is DateTime dt)
                        {
                            try
                            {
                                dt.EnsureUtc(nameof(dt));
                            }
                            catch (NgbArgumentInvalidException)
                            {
                                throw new ReferenceRegisterRecordsValidationException(
                                    registerId,
                                    reason: "datetime_not_utc",
                                    details: new { fieldCodeNorm = f.CodeNorm, actualKind = dt.Kind });
                            }
                        }
                    }

                    p.Add(paramName, v);
                }

                sql.Append(BuildValuesRow(fields, i));
            }

            sql.Append(';');

            var cmd = new CommandDefinition(sql.ToString(), p, transaction: uow.Transaction, cancellationToken: ct);
            await uow.Connection.ExecuteAsync(cmd);
        }
    }

    public async Task AppendTombstonesForRecorderAsync(
        Guid registerId,
        Guid recorderDocumentId,
        IReadOnlyCollection<Guid>? keepDimensionSetIds,
        CancellationToken ct = default)
    {
        registerId.EnsureNonEmpty(nameof(registerId));
        recorderDocumentId.EnsureNonEmpty(nameof(recorderDocumentId));
        await uow.EnsureOpenForTransactionAsync(ct);

        // Ensure physical schema before inserting.
        await EnsureSchemaAsync(registerId, ct);

        var reg = await registers.GetByIdAsync(registerId, ct)
                  ?? throw new ReferenceRegisterNotFoundException(registerId);

        // Only SubordinateToRecorder registers support recorder tombstones.
        if (reg.RecordMode != ReferenceRegisterRecordMode.SubordinateToRecorder)
            return;

        var table = ReferenceRegisterNaming.RecordsTable(reg.TableCode);
        ReferenceRegisterSqlIdentifiers.EnsureOrThrow(table, "records table");

        var fields = await fieldsRepo.GetByRegisterIdAsync(registerId, ct);
        foreach (var f in fields)
        {
            ReferenceRegisterSqlIdentifiers.EnsureOrThrow(f.ColumnCode, "field column_code");
        }

        // Mark registry as having records (enables metadata guards).
        // Safe idempotent update.
        {
            const string sql = "UPDATE reference_registers SET has_records = TRUE, updated_at_utc = NOW() WHERE register_id = @Id AND has_records = FALSE;";
            var cmd = new CommandDefinition(sql, new { Id = registerId }, transaction: uow.Transaction, cancellationToken: ct);
            await uow.Connection.ExecuteAsync(cmd);
        }

        var fieldCols = fields.Count == 0
            ? string.Empty
            : ", " + string.Join(", ", fields.Select(f => $"t.{f.ColumnCode}"));

        var insertCols = fields.Count == 0
            ? string.Empty
            : ", " + string.Join(", ", fields.Select(f => f.ColumnCode));

        // Periodic nuance:
        // - SliceLast selects the effective version by (period_utc <= asOfUtc) and prefers later period_utc.
        // - Therefore, to prevent any recorder-produced version from resurfacing in the future,
        //   we must be tombstone EACH effective version (by period) produced by the recorder.
        // - For NonPeriodic registers, there is only one effective version per key.

        var distinctOn = reg.Periodicity == ReferenceRegisterPeriodicity.NonPeriodic
            ? "(t.dimension_set_id, t.recorder_document_id)"
            : "(t.dimension_set_id, t.recorder_document_id, t.period_bucket_utc, t.period_utc)";

        var orderBy = reg.Periodicity == ReferenceRegisterPeriodicity.NonPeriodic
            ? "t.dimension_set_id, t.recorder_document_id, t.recorded_at_utc DESC, t.record_id DESC"
            : "t.dimension_set_id, t.recorder_document_id, t.period_bucket_utc DESC, t.period_utc DESC, t.recorded_at_utc DESC, t.record_id DESC";

        // If the caller passes an empty set, treat it as “tombstone all keys”.
        var keep = keepDimensionSetIds is { Count: > 0 }
            ? keepDimensionSetIds.Distinct().ToArray()
            : null;

        var keepFilter = keep is null
            ? string.Empty
            : "AND NOT (dimension_set_id = ANY(@KeepDimensionSetIds))";

        var sqlInsert = $"""
                         WITH last_rows AS (
                             SELECT DISTINCT ON {distinctOn}
                                 t.dimension_set_id,
                                 t.period_utc,
                                 t.period_bucket_utc,
                                 t.recorder_document_id,
                                 t.is_deleted{fieldCols}
                             FROM {table} t
                             WHERE t.recorder_document_id = @RecorderDocumentId
                             ORDER BY {orderBy}
                         )
                         INSERT INTO {table} (
                             dimension_set_id,
                             period_utc,
                             period_bucket_utc,
                             recorder_document_id,
                             is_deleted{insertCols}
                         )
                         SELECT
                             dimension_set_id,
                             period_utc,
                             period_bucket_utc,
                             recorder_document_id,
                             TRUE{(fields.Count == 0 ? string.Empty : ", " + string.Join(", ", fields.Select(f => f.ColumnCode)))}
                         FROM last_rows
                         WHERE is_deleted = FALSE
                         {keepFilter};
                         """;

        var cmdInsert = new CommandDefinition(
            sqlInsert,
            new
            {
                RecorderDocumentId = recorderDocumentId,
                KeepDimensionSetIds = keep
            },
            transaction: uow.Transaction,
            cancellationToken: ct);

        await uow.Connection.ExecuteAsync(cmdInsert);
    }

    private static void ValidateRecords(
        Guid registerId,
        ReferenceRegisterAdminItem reg,
        IReadOnlyList<ReferenceRegisterRecordWrite> records,
        IReadOnlyDictionary<string, ReferenceRegisterField> fieldByCodeNorm)
    {
        for (var i = 0; i < records.Count; i++)
        {
            var r = records[i];

            if (r.Values is null)
                throw new ReferenceRegisterRecordsValidationException(registerId, reason: "values_null", details: new { index = i });

            // Validate recorder mode.
            if (reg.RecordMode == ReferenceRegisterRecordMode.SubordinateToRecorder)
            {
                if (r.RecorderDocumentId is null || r.RecorderDocumentId.Value == Guid.Empty)
                    throw new ReferenceRegisterRecordsValidationException(registerId, reason: "recorder_required", details: new { index = i, recordMode = reg.RecordMode });
            }
            else
            {
                if (r.RecorderDocumentId is not null)
                    throw new ReferenceRegisterRecordsValidationException(registerId, reason: "recorder_forbidden", details: new { index = i });
            }

            // Validate periodicity.
            if (reg.Periodicity == ReferenceRegisterPeriodicity.NonPeriodic)
            {
                if (r.PeriodUtc is not null)
                    throw new ReferenceRegisterRecordsValidationException(registerId,
                        reason: "period_not_allowed_for_non_periodic",
                        details: new { index = i, periodUtc = r.PeriodUtc });
            }
            else
            {
                if (r.PeriodUtc is null)
                    throw new ReferenceRegisterRecordsValidationException(registerId,
                        reason: "period_required_for_periodic",
                        details: new { index = i, periodicity = reg.Periodicity });

                try
                {
                    r.PeriodUtc.Value.EnsureUtc(nameof(r.PeriodUtc));
                }
                catch (NgbArgumentInvalidException)
                {
                    throw new ReferenceRegisterRecordsValidationException(
                        registerId,
                        reason: "period_not_utc",
                        details: new { index = i, periodUtc = r.PeriodUtc, actualKind = r.PeriodUtc.Value.Kind });
                }
            }

            // Validate fields: no unknown keys.
            foreach (var (k, _) in r.Values)
            {
                if (k is null)
                    throw new ReferenceRegisterRecordsValidationException(registerId, reason: "field_key_null",
                        details: new { index = i });

                var key = k.Trim();
                if (key.Length == 0)
                    throw new ReferenceRegisterRecordsValidationException(registerId, reason: "field_key_empty",
                        details: new { index = i });

                if (!fieldByCodeNorm.ContainsKey(key))
                    throw new ReferenceRegisterRecordsValidationException(registerId, reason: "unknown_field",
                        details: new { index = i, fieldCodeNorm = key });
            }
        }
    }

    private async Task EnsureRecordsTableAsync(string table, CancellationToken ct)
    {
        // Base table must exist before we can ALTER / CREATE TRIGGER.
        var sql = $"""
                   CREATE TABLE IF NOT EXISTS {table} (
                       record_id BIGSERIAL PRIMARY KEY,

                       dimension_set_id UUID NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000'
                           REFERENCES platform_dimension_sets(dimension_set_id),

                       period_utc TIMESTAMPTZ NULL,
                       period_bucket_utc TIMESTAMPTZ NULL,

                       recorder_document_id UUID NULL
                           REFERENCES documents(id),

                       recorded_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                       is_deleted BOOLEAN NOT NULL DEFAULT FALSE
                   );
                   """;

        var cmd = new CommandDefinition(sql, transaction: uow.Transaction, cancellationToken: ct);
        await uow.Connection.ExecuteAsync(cmd);
    }

    private async Task EnsureBaseColumnConstraintsAsync(string table, ReferenceRegisterAdminItem reg,
        CancellationToken ct)
    {
        // Column NOT NULL drift repair (used for performance and query plans).
        var sb = new StringBuilder();

        if (reg.Periodicity == ReferenceRegisterPeriodicity.NonPeriodic)
        {
            sb.AppendLine($"ALTER TABLE {table} ALTER COLUMN period_utc DROP NOT NULL;");
            sb.AppendLine($"ALTER TABLE {table} ALTER COLUMN period_bucket_utc DROP NOT NULL;");
        }
        else
        {
            sb.AppendLine($"ALTER TABLE {table} ALTER COLUMN period_utc SET NOT NULL;");
            sb.AppendLine($"ALTER TABLE {table} ALTER COLUMN period_bucket_utc SET NOT NULL;");
        }

        if (reg.RecordMode == ReferenceRegisterRecordMode.SubordinateToRecorder)
            sb.AppendLine($"ALTER TABLE {table} ALTER COLUMN recorder_document_id SET NOT NULL;");
        else
            sb.AppendLine($"ALTER TABLE {table} ALTER COLUMN recorder_document_id DROP NOT NULL;");

        if (sb.Length > 0)
        {
            var cmd = new CommandDefinition(sb.ToString(), transaction: uow.Transaction, cancellationToken: ct);
            await uow.Connection.ExecuteAsync(cmd);
        }

        // Semantic drift repair (enforce NULL where NULL is required).
        await EnsureSemanticCheckConstraintsAsync(table, reg, ct);
    }

    private async Task EnsureSemanticCheckConstraintsAsync(
        string table,
        ReferenceRegisterAdminItem reg,
        CancellationToken ct)
    {
        // Independent registers must not store recorder_document_id at all.
        var ckRecorderNull = Ck("ck_refreg_recorder_null_", table);

        if (reg.RecordMode == ReferenceRegisterRecordMode.SubordinateToRecorder)
        {
            // Recorder is required; the NOT NULL constraint is enough.
            await DropConstraintIfExistsAsync(table, ckRecorderNull, ct);
        }
        else
        {
            await EnsureCheckConstraintAsync(
                table,
                ckRecorderNull,
                "recorder_document_id IS NULL",
                ct);
        }

        // NonPeriodic registers must not store period columns.
        var ckNonPeriodic = Ck("ck_refreg_nonperiodic_", table);

        if (reg.Periodicity == ReferenceRegisterPeriodicity.NonPeriodic)
        {
            await EnsureCheckConstraintAsync(
                table,
                ckNonPeriodic,
                "period_utc IS NULL AND period_bucket_utc IS NULL",
                ct);
        }
        else
        {
            await DropConstraintIfExistsAsync(table, ckNonPeriodic, ct);
        }
    }

    private async Task EnsureCheckConstraintAsync(
        string table,
        string constraintName,
        string expression,
        CancellationToken ct)
    {
        // PostgreSQL doesn't support "ADD CONSTRAINT IF NOT EXISTS", so we emulate it.
        var sql = $"""
                   DO $$
                   BEGIN
                       IF NOT EXISTS (
                           SELECT 1
                           FROM pg_constraint
                           WHERE conname = '{constraintName}'
                             AND conrelid = '{table}'::regclass
                       ) THEN
                           ALTER TABLE {table} ADD CONSTRAINT {constraintName} CHECK ({expression});
                       END IF;
                   END
                   $$;
                   """;

        var cmd = new CommandDefinition(sql, transaction: uow.Transaction, cancellationToken: ct);
        await uow.Connection.ExecuteAsync(cmd);
    }

    private async Task DropConstraintIfExistsAsync(string table, string constraintName, CancellationToken ct)
    {
        var sql = $"ALTER TABLE {table} DROP CONSTRAINT IF EXISTS {constraintName};";
        var cmd = new CommandDefinition(sql, transaction: uow.Transaction, cancellationToken: ct);
        await uow.Connection.ExecuteAsync(cmd);
    }


    private async Task EnsureFieldColumnsAsync(
        string table,
        IReadOnlyList<ReferenceRegisterField> fields,
        bool hasRecords,
        CancellationToken ct)
    {
        if (fields.Count == 0)
            return;

        // Drift-repair for field columns:
        // - add missing columns
        // - align column type and nullability to metadata
        var existing = (await uow.Connection.QueryAsync<ColumnMeta>(
                new CommandDefinition(
                    """
                    SELECT
                        column_name       AS "ColumnName",
                        is_nullable       AS "IsNullable",
                        udt_name          AS "UdtName",
                        numeric_precision AS "NumericPrecision",
                        numeric_scale     AS "NumericScale"
                    FROM information_schema.columns
                    WHERE table_schema = 'public'
                      AND table_name = @Table;
                    """,
                    new { Table = table },
                    transaction: uow.Transaction,
                    cancellationToken: ct)))
            .ToDictionary(x => x.ColumnName, StringComparer.Ordinal);

        foreach (var f in fields)
        {
            ReferenceRegisterSqlIdentifiers.EnsureOrThrow(f.ColumnCode, "field column_code");

            var sqlType = ToSqlType(f.ColumnType);

            if (!existing.TryGetValue(f.ColumnCode, out var col))
            {
                if (hasRecords && !f.IsNullable)
                {
                    throw new ReferenceRegisterSchemaDriftAfterRecordsExistException(f.RegisterId, table,
                        reason: "missing_not_null_column",
                        details: new { column = f.ColumnCode, expectedSqlType = sqlType });
                }

                var nullable = f.IsNullable ? "NULL" : "NOT NULL";

                var sqlAdd = $"ALTER TABLE {table} ADD COLUMN IF NOT EXISTS {f.ColumnCode} {sqlType} {nullable};";
                await uow.Connection.ExecuteAsync(new CommandDefinition(sqlAdd, transaction: uow.Transaction, cancellationToken: ct));
                continue;
            }

            // Type drift repair.
            if (!ColumnTypeMatches(col, f.ColumnType))
            {
                if (hasRecords)
                {
                    throw new ReferenceRegisterSchemaDriftAfterRecordsExistException(f.RegisterId, table,
                        reason: "type_mismatch",
                        details: new { column = f.ColumnCode, expectedSqlType = sqlType, actualUdtName = col.UdtName });
                }

                var sqlAlterType = $"ALTER TABLE {table} ALTER COLUMN {f.ColumnCode} TYPE {sqlType} USING {f.ColumnCode}::{sqlType};";
                await uow.Connection.ExecuteAsync(new CommandDefinition(sqlAlterType, transaction: uow.Transaction, cancellationToken: ct));
            }

            // Nullability drift repair.
            var isNullable = string.Equals(col.IsNullable, "YES", StringComparison.OrdinalIgnoreCase);
            if (f.IsNullable && !isNullable)
            {
                if (hasRecords)
                {
                    throw new ReferenceRegisterSchemaDriftAfterRecordsExistException(f.RegisterId, table,
                        reason: "nullability_mismatch",
                        details: new { column = f.ColumnCode, expectedNullable = true, actualNullable = false });
                }

                var sqlDropNotNull = $"ALTER TABLE {table} ALTER COLUMN {f.ColumnCode} DROP NOT NULL;";
                await uow.Connection.ExecuteAsync(new CommandDefinition(sqlDropNotNull, transaction: uow.Transaction, cancellationToken: ct));
            }
            else if (!f.IsNullable && isNullable)
            {
                if (hasRecords)
                {
                    throw new ReferenceRegisterSchemaDriftAfterRecordsExistException(f.RegisterId, table,
                        reason: "nullability_mismatch",
                        details: new { column = f.ColumnCode, expectedNullable = false, actualNullable = true });
                }

                var sqlSetNotNull = $"ALTER TABLE {table} ALTER COLUMN {f.ColumnCode} SET NOT NULL;";
                await uow.Connection.ExecuteAsync(new CommandDefinition(sqlSetNotNull, transaction: uow.Transaction, cancellationToken: ct));
            }
        }
    }

    private async Task EnsureIndexesAsync(string table, ReferenceRegisterAdminItem reg, CancellationToken ct)
    {
        // Indexes are part of the *per-register* physical table contract.
        // We keep them deterministic (hashed names) and create them idempotently.

        // 1) Covering key index: optimized for SliceLast, SliceLastAll (DISTINCT ON) and KeyHistory.
        //    Key shape:
        //      - NonPeriodic: (dimension_set_id, recorder_document_id)
        //      - Periodic:    (dimension_set_id, recorder_document_id, period_bucket_utc)
        var ixKeyV2 = Ix("ix_refreg_key_v2_", table);

        var keyCols = reg.Periodicity == ReferenceRegisterPeriodicity.NonPeriodic
            ? "(dimension_set_id, recorder_document_id, recorded_at_utc DESC, record_id DESC)"
            : "(dimension_set_id, recorder_document_id, period_bucket_utc DESC, period_utc DESC, recorded_at_utc DESC, record_id DESC)";

        var sqlKeyV2 = $"CREATE INDEX IF NOT EXISTS {ixKeyV2} ON {table} {keyCols};";
        await uow.Connection.ExecuteAsync(new CommandDefinition(sqlKeyV2, transaction: uow.Transaction, cancellationToken: ct));

        // 2) Recorder scan index: optimized for tombstone generation (Unpost/Repost) and recorder-scoped slices.
        //    We only create it for SubordinateToRecorder registers.
        if (reg.RecordMode == ReferenceRegisterRecordMode.SubordinateToRecorder)
        {
            var ixRecorderKeyV2 = Ix("ix_refreg_recorder_key_v2_", table);

            var recorderCols = reg.Periodicity == ReferenceRegisterPeriodicity.NonPeriodic
                ? "(recorder_document_id, dimension_set_id, recorded_at_utc DESC, record_id DESC)"
                : "(recorder_document_id, dimension_set_id, period_bucket_utc DESC, period_utc DESC, recorded_at_utc DESC, record_id DESC)";

            var sqlRecorderKeyV2 = $"CREATE INDEX IF NOT EXISTS {ixRecorderKeyV2} ON {table} {recorderCols};";
            await uow.Connection.ExecuteAsync(new CommandDefinition(sqlRecorderKeyV2, transaction: uow.Transaction, cancellationToken: ct));
        }
    }

    private sealed record ColumnMeta(
        string ColumnName,
        string IsNullable,
        string UdtName,
        int? NumericPrecision,
        int? NumericScale);

    private static bool ColumnTypeMatches(ColumnMeta meta, ColumnType t)
    {
        var expectedUdt = t switch
        {
            ColumnType.String => "text",
            ColumnType.Int32 => "int4",
            ColumnType.Int64 => "int8",
            ColumnType.Decimal => "numeric",
            ColumnType.Boolean => "bool",
            ColumnType.Guid => "uuid",
            ColumnType.Date => "date",
            ColumnType.DateTimeUtc => "timestamptz",
            ColumnType.Json => "jsonb",
            _ => throw new NgbInvariantViolationException($"Unsupported ColumnType '{t}'.",
                new Dictionary<string, object?> { ["columnType"] = t.ToString() })
        };

        if (!string.Equals(meta.UdtName, expectedUdt, StringComparison.OrdinalIgnoreCase))
            return false;

        // Precision/scale drift repair for NUMERIC columns.
        if (t == ColumnType.Decimal)
        {
            // We require a stable decimal contract: NUMERIC(28,8).
            // If the column is plain NUMERIC without explicit precision/scale, information_schema may return NULLs.
            // Treat that as drift.
            if (meta.NumericPrecision is null || meta.NumericPrecision != 28)
                return false;

            if (meta.NumericScale is null || meta.NumericScale != 8)
                return false;
        }

        return true;
    }

    private static string ToSqlType(ColumnType t) => t switch
    {
        ColumnType.String => "TEXT",
        ColumnType.Int32 => "INTEGER",
        ColumnType.Int64 => "BIGINT",
        ColumnType.Decimal => "NUMERIC(28,8)",
        ColumnType.Boolean => "BOOLEAN",
        ColumnType.Guid => "UUID",
        ColumnType.Date => "DATE",
        ColumnType.DateTimeUtc => "TIMESTAMPTZ",
        ColumnType.Json => "JSONB",
        _ => throw new NgbInvariantViolationException($"Unsupported ColumnType '{t}'.",
            new Dictionary<string, object?> { ["columnType"] = t.ToString() })
    };

    private static string BuildInsertPrefix(string table, IReadOnlyList<ReferenceRegisterField> fields)
    {
        var cols = new List<string>(capacity: 5 + fields.Count)
        {
            "dimension_set_id",
            "period_utc",
            "period_bucket_utc",
            "recorder_document_id",
            "is_deleted"
        };

        cols.AddRange(fields.Select(f => f.ColumnCode));

        return $"INSERT INTO {table} (" + string.Join(", ", cols) + ") VALUES ";
    }

    private static string BuildValuesRow(IReadOnlyList<ReferenceRegisterField> fields, int i)
    {
        var values = new List<string>(capacity: 5 + fields.Count)
        {
            "@" + P("DimensionSetId", i),
            "@" + P("PeriodUtc", i),
            "@" + P("PeriodBucketUtc", i),
            "@" + P("RecorderDocumentId", i),
            "@" + P("IsDeleted", i)
        };

        foreach (var f in fields)
        {
            var p = "@" + P(Param(f.ColumnCode), i);
            if (f.ColumnType == ColumnType.Json)
                p += "::jsonb";
            
            values.Add(p);
        }

        return "(" + string.Join(", ", values) + ")";
    }

    private static string P(string name, int i) => $"{name}_{i}";

    private static string Param(string columnCode) => "p_" + columnCode;

    private static string Ix(string prefix, string table)
    {
        // Keep names short and deterministic under 63 chars.
        // We hash the table name to avoid collisions across different registers.
        var token = DeterministicGuid.Create($"Ix|{prefix}|{table}").ToString("N")[..12];
        var name = prefix + token;
        if (name.Length > ReferenceRegisterSqlIdentifiers.MaxIdentifierLength)
            name = name[..ReferenceRegisterSqlIdentifiers.MaxIdentifierLength];

        ReferenceRegisterSqlIdentifiers.EnsureOrThrow(name, "index name");
        return name;
    }

    private static string Trg(string prefix, string table)
    {
        var token = DeterministicGuid.Create($"Trg|{prefix}|{table}").ToString("N")[..12];
        var name = prefix + token;
        if (name.Length > ReferenceRegisterSqlIdentifiers.MaxIdentifierLength)
            name = name[..ReferenceRegisterSqlIdentifiers.MaxIdentifierLength];

        ReferenceRegisterSqlIdentifiers.EnsureOrThrow(name, "trigger name");
        return name;
    }

    private static string Ck(string prefix, string table)
    {
        var token = DeterministicGuid.Create($"Ck|{prefix}|{table}").ToString("N")[..12];
        var name = prefix + token;
        if (name.Length > ReferenceRegisterSqlIdentifiers.MaxIdentifierLength)
            name = name[..ReferenceRegisterSqlIdentifiers.MaxIdentifierLength];

        ReferenceRegisterSqlIdentifiers.EnsureOrThrow(name, "constraint name");
        return name;
    }
}
