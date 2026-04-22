using System.Diagnostics;
using Dapper;
using Microsoft.Extensions.Logging;
using NGB.Metadata.Base;
using NGB.Metadata.Schema;
using NGB.Persistence.Schema;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Schema.Internal;
using NGB.ReferenceRegisters;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.Schema;

/// <summary>
/// PostgreSQL validator for the Reference Registers core schema.
///
/// This validator is intentionally conservative: it fails fast when the platform
/// can't safely run (correctness + safety invariants for append-only per-register record tables).
/// </summary>
public sealed class PostgresReferenceRegistersCoreSchemaValidationService(
    IDbSchemaInspector schemaInspector,
    IUnitOfWork uow,
    ILogger<PostgresReferenceRegistersCoreSchemaValidationService> logger)
    : IReferenceRegistersCoreSchemaValidationService
{
    public async Task ValidateAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        var snapshot = await schemaInspector.GetSnapshotAsync(ct);
        var errors = new List<string>();

        // 1) Core metadata tables
        PostgresSchemaValidationChecks.RequireTable(snapshot, "reference_registers", errors);
        PostgresSchemaValidationChecks.RequireTable(snapshot, "reference_register_fields", errors);
        PostgresSchemaValidationChecks.RequireTable(snapshot, "reference_register_dimension_rules", errors);
        PostgresSchemaValidationChecks.RequireTable(snapshot, "reference_register_write_state", errors);

        // Required platform tables for FKs
        PostgresSchemaValidationChecks.RequireTable(snapshot, "platform_dimensions", errors);
        PostgresSchemaValidationChecks.RequireTable(snapshot, "platform_dimension_sets", errors);
        PostgresSchemaValidationChecks.RequireTable(snapshot, "documents", errors);

        // 2) Minimal column contracts
        PostgresSchemaValidationChecks.RequireColumns(
            snapshot,
            tableName: "reference_registers",
            required:
            [
                "register_id",
                "code",
                "code_norm",
                "name",
                "table_code",
                "periodicity",
                "record_mode",
                "has_records"
            ],
            errors);

        PostgresSchemaValidationChecks.RequireColumns(
            snapshot,
            tableName: "reference_register_fields",
            required:
            [
                "register_id",
                "code",
                "code_norm",
                "column_code",
                "name",
                "ordinal",
                "column_type",
                "is_nullable"
            ],
            errors);

        PostgresSchemaValidationChecks.RequireColumns(
            snapshot,
            tableName: "reference_register_dimension_rules",
            required:
            [
                "register_id",
                "dimension_id",
                "ordinal",
                "is_required"
            ],
            errors);

        PostgresSchemaValidationChecks.RequireColumns(
            snapshot,
            tableName: "reference_register_write_state",
            required:
            [
                "register_id",
                "document_id",
                "operation",
                "started_at_utc",
                "completed_at_utc"
            ],
            errors);

        // 3) Critical indexes (names are part of the contract; migrations are idempotent)
        PostgresSchemaValidationChecks.RequireIndex(snapshot, "reference_registers", "ux_reference_registers_code_norm", errors);
        PostgresSchemaValidationChecks.RequireIndex(snapshot, "reference_registers", "ux_reference_registers_table_code", errors);

        PostgresSchemaValidationChecks.RequireIndex(snapshot, "reference_register_fields", "ix_refreg_fields_register_ordinal", errors);
        PostgresSchemaValidationChecks.RequireIndex(snapshot, "reference_register_dimension_rules", "ix_refreg_dim_rules_register_ordinal", errors);
        PostgresSchemaValidationChecks.RequireIndex(snapshot, "reference_register_write_state", "ix_refreg_write_log_document", errors);

        // 4) Critical unique constraints (surfaced as indexes)
        PostgresSchemaValidationChecks.RequireIndex(snapshot, "reference_register_fields", "ux_reference_register_fields__register_code_norm", errors);
        PostgresSchemaValidationChecks.RequireIndex(snapshot, "reference_register_fields", "ux_reference_register_fields__register_ordinal", errors);
        PostgresSchemaValidationChecks.RequireIndex(snapshot, "reference_register_dimension_rules", "ux_reference_register_dimension_rules__register_ordinal", errors);

        // 5) Critical foreign keys
        PostgresSchemaValidationChecks.RequireForeignKey(snapshot, "reference_register_fields", "register_id", "reference_registers", "register_id", errors);
        PostgresSchemaValidationChecks.RequireForeignKey(snapshot, "reference_register_dimension_rules", "register_id", "reference_registers", "register_id", errors);
        PostgresSchemaValidationChecks.RequireForeignKey(snapshot, "reference_register_dimension_rules", "dimension_id", "platform_dimensions", "dimension_id", errors);
        PostgresSchemaValidationChecks.RequireForeignKey(snapshot, "reference_register_write_state", "register_id", "reference_registers", "register_id", errors);
        PostgresSchemaValidationChecks.RequireForeignKey(snapshot, "reference_register_write_state", "document_id", "documents", "id", errors);

        // 6) DB-level guards and invariants
        await uow.EnsureConnectionOpenAsync(ct);

        // 6.1) Append-only guard function must exist (used by per-register __records tables)
        await PostgresSchemaValidationChecks.RequireFunctionAsync(uow, "ngb_forbid_mutation_of_append_only_table", errors, ct);

        // 6.2) Immutability guards after has_records
        await PostgresSchemaValidationChecks.RequireFunctionAsync(uow, "ngb_refreg_forbid_register_mutation_when_has_records", errors, ct);
        await PostgresSchemaValidationChecks.RequireTriggerAsync(uow, "trg_refreg_registers_immutable_when_has_records", "reference_registers", errors, ct);

        await PostgresSchemaValidationChecks.RequireFunctionAsync(uow, "ngb_refreg_forbid_field_mutation_when_has_records", errors, ct);
        await PostgresSchemaValidationChecks.RequireTriggerAsync(uow, "trg_refreg_fields_immutable_when_has_records", "reference_register_fields", errors, ct);

        await PostgresSchemaValidationChecks.RequireFunctionAsync(uow, "ngb_refreg_forbid_dim_rule_mutation_when_has_records", errors, ct);
        await PostgresSchemaValidationChecks.RequireTriggerAsync(uow, "trg_refreg_dim_rules_immutable_when_has_records", "reference_register_dimension_rules", errors, ct);

        // 7) Per-register physical tables for registers that already have records.
        await ValidatePerRegisterPhysicalTablesAsync(snapshot, errors, ct);

        if (errors.Count > 0)
        {
            logger.LogError(
                "Reference registers core schema validation FAILED with {ErrorCount} errors in {ElapsedMs} ms.",
                errors.Count,
                sw.ElapsedMilliseconds);

            throw new NgbConfigurationViolationException("Reference registers core schema validation failed:\n- " + string.Join("\n- ", errors));
        }

        logger.LogInformation("Reference registers core schema validation OK in {ElapsedMs} ms.", sw.ElapsedMilliseconds);
    }

    private async Task ValidatePerRegisterPhysicalTablesAsync(
        DbSchemaSnapshot snapshot,
        List<string> errors,
        CancellationToken ct)
    {
        // We validate physical __records tables only when the register is marked as having records.
        // This keeps bootstrap lightweight and avoids forcing early table creation for brand-new registers.
        var regs = (await uow.Connection.QueryAsync<RegisterRow>(
                new CommandDefinition(
                    """
                    SELECT
                        register_id AS "RegisterId",
                        table_code  AS "TableCode",
                        periodicity AS "Periodicity",
                        record_mode AS "RecordMode",
                        has_records AS "HasRecords"
                    FROM reference_registers
                    WHERE has_records = TRUE;
                    """,
                    transaction: uow.Transaction,
                    cancellationToken: ct)))
            .ToList();

        if (regs.Count == 0)
            return;

        // Load all field definitions for those registers in one round-trip.
        var regIds = regs.Select(r => r.RegisterId).ToArray();

        var fields = (await uow.Connection.QueryAsync<FieldRow>(
                new CommandDefinition(
                    """
                    SELECT
                        register_id  AS "RegisterId",
                        code_norm    AS "CodeNorm",
                        column_code  AS "ColumnCode",
                        column_type  AS "ColumnType",
                        is_nullable  AS "IsNullable"
                    FROM reference_register_fields
                    WHERE register_id = ANY(@Ids);
                    """,
                    new { Ids = regIds },
                    transaction: uow.Transaction,
                    cancellationToken: ct)))
            .ToList();

        var fieldsByReg = fields
            .GroupBy(f => f.RegisterId)
            .ToDictionary(g => g.Key, IReadOnlyList<FieldRow> (g) => g.ToList());

        foreach (var r in regs)
        {
            var table = ReferenceRegisterNaming.RecordsTable(r.TableCode);

            if (!snapshot.Tables.Contains(table))
            {
                errors.Add($"Reference register '{r.RegisterId}' is marked has_records=true but physical table '{table}' is missing.");
                continue;
            }

            // Base columns + nullability + FKs
            ValidateBaseColumns(snapshot, table, r, errors);

            // Check constraints (semantic invariants)
            await ValidateSemanticConstraintsAsync(table, r, errors, ct);

            // Append-only trigger must exist (trigger name is hashed; validate by function binding).
            await ValidateAppendOnlyGuardTriggerAsync(table, errors, ct);

            // Per-register indexes are part of the physical contract.
            ValidatePerRegisterIndexes(snapshot, table, r, errors);

            // Field columns: existence, type, nullability (including decimal precision/scale).
            fieldsByReg.TryGetValue(r.RegisterId, out var f);
            await ValidateFieldColumnsAsync(table, f ?? Array.Empty<FieldRow>(), errors, ct);
        }
    }

    private static void ValidateBaseColumns(DbSchemaSnapshot snapshot, string table, RegisterRow r, List<string> errors)
    {
        if (!snapshot.ColumnsByTable.TryGetValue(table, out var cols))
        {
            errors.Add($"Cannot read columns for table '{table}'.");
            return;
        }

        DbColumnSchema? Col(string name) =>
            cols.FirstOrDefault(c => c.ColumnName.Equals(name, StringComparison.OrdinalIgnoreCase));

        // record_id BIGSERIAL (data_type = bigint)
        RequireColumnType(table, Col("record_id"), expectedDbType: "bigint", expectedNullable: false, errors);
        RequireColumnType(table, Col("dimension_set_id"), expectedDbType: "uuid", expectedNullable: false, errors);

        // period columns vary by periodicity
        var periodNullable = r.Periodicity == ReferenceRegisterPeriodicity.NonPeriodic;
        RequireColumnType(table, Col("period_utc"), expectedDbType: "timestamp with time zone", expectedNullable: periodNullable, errors);
        RequireColumnType(table, Col("period_bucket_utc"), expectedDbType: "timestamp with time zone", expectedNullable: periodNullable, errors);

        // recorder_document_id varies by record mode
        var recorderNullable = r.RecordMode != ReferenceRegisterRecordMode.SubordinateToRecorder;
        RequireColumnType(table, Col("recorder_document_id"), expectedDbType: "uuid", expectedNullable: recorderNullable, errors);

        RequireColumnType(table, Col("recorded_at_utc"), expectedDbType: "timestamp with time zone", expectedNullable: false, errors);
        RequireColumnType(table, Col("is_deleted"), expectedDbType: "boolean", expectedNullable: false, errors);

        // Foreign keys are part of the core safety contract.
        PostgresSchemaValidationChecks.RequireForeignKey(snapshot, table, "dimension_set_id", "platform_dimension_sets", "dimension_set_id", errors);
        PostgresSchemaValidationChecks.RequireForeignKey(snapshot, table, "recorder_document_id", "documents", "id", errors);
    }

    private static void RequireColumnType(
        string table,
        DbColumnSchema? col,
        string expectedDbType,
        bool expectedNullable,
        List<string> errors)
    {
        if (col is null)
        {
            errors.Add($"Table '{table}' is missing required column.");
            return;
        }

        if (!string.Equals(col.DbType, expectedDbType, StringComparison.OrdinalIgnoreCase))
            errors.Add($"Table '{table}' column '{col.ColumnName}' has type '{col.DbType}', expected '{expectedDbType}'.");

        if (col.IsNullable != expectedNullable)
        {
            errors.Add(
                $"Table '{table}' column '{col.ColumnName}' nullability mismatch. " +
                $"Expected {(expectedNullable ? "NULL" : "NOT NULL")}.");
        }
    }

    private async Task ValidateSemanticConstraintsAsync(
        string table,
        RegisterRow reg,
        List<string> errors,
        CancellationToken ct)
    {
        var defs = (await uow.Connection.QueryAsync<string>(
                new CommandDefinition(
                    """
                    SELECT pg_get_constraintdef(c.oid)
                    FROM pg_constraint c
                    JOIN pg_class cl ON cl.oid = c.conrelid
                    JOIN pg_namespace ns ON ns.oid = cl.relnamespace
                    WHERE ns.nspname = 'public'
                      AND cl.relname = @table
                      AND c.contype = 'c';
                    """,
                    new { table },
                    transaction: uow.Transaction,
                    cancellationToken: ct)))
            .ToList();

        var defsText = string.Join("\n", defs).ToLowerInvariant();

        if (reg.RecordMode != ReferenceRegisterRecordMode.SubordinateToRecorder)
        {
            if (!defsText.Contains("recorder_document_id is null"))
                errors.Add($"Table '{table}' is missing semantic CHECK constraint enforcing recorder_document_id IS NULL (Independent register).");
        }

        if (reg.Periodicity == ReferenceRegisterPeriodicity.NonPeriodic)
        {
            if (!defsText.Contains("period_utc is null") || !defsText.Contains("period_bucket_utc is null"))
                errors.Add($"Table '{table}' is missing semantic CHECK constraint enforcing period_utc IS NULL AND period_bucket_utc IS NULL (NonPeriodic register).");
        }
    }

    private async Task ValidateAppendOnlyGuardTriggerAsync(string table, List<string> errors, CancellationToken ct)
    {
        var exists = await uow.Connection.ExecuteScalarAsync<int>(
            new CommandDefinition(
                """
                SELECT COUNT(*)
                FROM pg_trigger t
                JOIN pg_class cl ON cl.oid = t.tgrelid
                JOIN pg_namespace ns ON ns.oid = cl.relnamespace
                JOIN pg_proc p ON p.oid = t.tgfoid
                WHERE ns.nspname = 'public'
                  AND cl.relname = @table
                  AND NOT t.tgisinternal
                  AND p.proname = 'ngb_forbid_mutation_of_append_only_table';
                """,
                new { table },
                transaction: uow.Transaction,
                cancellationToken: ct));

        if (exists == 0)
            errors.Add($"Table '{table}' is missing append-only trigger (ngb_forbid_mutation_of_append_only_table).");
    }

    private static void ValidatePerRegisterIndexes(
        DbSchemaSnapshot snapshot,
        string table,
        RegisterRow reg,
        List<string> errors)
    {
        snapshot.IndexesByTable.TryGetValue(table, out var idx);
        idx ??= [];

        // 1) Key v2 index.
        if (reg.Periodicity == ReferenceRegisterPeriodicity.NonPeriodic)
        {
            RequireIndexByColumns(
                idx,
                table,
                ["dimension_set_id", "recorder_document_id", "recorded_at_utc", "record_id"],
                errors,
                "key_v2 index (nonperiodic)");
        }
        else
        {
            RequireIndexByColumns(
                idx,
                table,
                [
                    "dimension_set_id", "recorder_document_id", "period_bucket_utc", "period_utc", "recorded_at_utc",
                    "record_id"
                ],
                errors,
                "key_v2 index (periodic)");
        }

        // 2) Recorder scan indexes for SubordinateToRecorder.
        if (reg.RecordMode == ReferenceRegisterRecordMode.SubordinateToRecorder)
        {
            if (reg.Periodicity == ReferenceRegisterPeriodicity.NonPeriodic)
            {
                RequireIndexByColumns(
                    idx,
                    table,
                    ["recorder_document_id", "dimension_set_id", "recorded_at_utc", "record_id"],
                    errors,
                    "recorder_key_v2 index (nonperiodic)");
            }
            else
            {
                RequireIndexByColumns(
                    idx,
                    table,
                    [
                        "recorder_document_id", "dimension_set_id", "period_bucket_utc", "period_utc",
                        "recorded_at_utc", "record_id"
                    ],
                    errors,
                    "recorder_key_v2 index (periodic)");
            }
        }
    }

    private static void RequireIndexByColumns(
        IReadOnlyList<DbIndexSchema> indexes,
        string table,
        string[] expectedColumns,
        List<string> errors,
        string description)
    {
        var exists = indexes.Any(i => ColumnsMatch(i.ColumnNames, expectedColumns));
        if (!exists)
            errors.Add($"Table '{table}' is missing {description}: expected index on ({string.Join(", ", expectedColumns)}).");
    }

    private static bool ColumnsMatch(IReadOnlyList<string> actual, string[] expected)
    {
        if (actual.Count != expected.Length)
            return false;

        for (var i = 0; i < expected.Length; i++)
        {
            if (!actual[i].Equals(expected[i], StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private async Task ValidateFieldColumnsAsync(
        string table,
        IReadOnlyList<FieldRow> fields,
        List<string> errors,
        CancellationToken ct)
    {
        if (fields.Count == 0)
            return;

        // Load column meta once per table and reuse for both type and nullability checks.
        var columnCodes = fields
            .Select(f => f.ColumnCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var meta = (await uow.Connection.QueryAsync<ColumnMeta>(
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
                      AND table_name = @Table
                      AND column_name = ANY(@Columns);
                    """,
                    new { Table = table, Columns = columnCodes },
                    transaction: uow.Transaction,
                    cancellationToken: ct)))
            .ToDictionary(x => x.ColumnName, StringComparer.OrdinalIgnoreCase);

        foreach (var f in fields)
        {
            if (!meta.TryGetValue(f.ColumnCode, out var col))
            {
                errors.Add($"Table '{table}' is missing field column '{f.ColumnCode}' (code_norm '{f.CodeNorm}').");
                continue;
            }

            var expectedType = (ColumnType)f.ColumnType;

            if (!ColumnTypeMatches(col, expectedType))
                errors.Add($"Table '{table}' column '{f.ColumnCode}' type mismatch. Expected {ToSqlType(expectedType)}.");

            var isNullable = string.Equals(col.IsNullable, "YES", StringComparison.OrdinalIgnoreCase);
            if (f.IsNullable != isNullable)
            {
                errors.Add(
                    $"Table '{table}' column '{f.ColumnCode}' nullability mismatch. " +
                    $"Expected {(f.IsNullable ? "NULL" : "NOT NULL")}.");
            }
        }
    }

    private sealed record RegisterRow(
        Guid RegisterId,
        string TableCode,
        ReferenceRegisterPeriodicity Periodicity,
        ReferenceRegisterRecordMode RecordMode,
        bool HasRecords);

    private sealed record FieldRow(
        Guid RegisterId,
        string CodeNorm,
        string ColumnCode,
        short ColumnType,
        bool IsNullable);

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
            _ => throw new NgbInvariantViolationException($"Unsupported ColumnType '{t}'.", new Dictionary<string, object?> { ["columnType"] = t.ToString() })
        };

        if (!string.Equals(meta.UdtName, expectedUdt, StringComparison.OrdinalIgnoreCase))
            return false;

        if (t == ColumnType.Decimal)
        {
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
        _ => throw new NgbInvariantViolationException($"Unsupported ColumnType '{t}'.", new Dictionary<string, object?> { ["columnType"] = t.ToString() })
    };
}
