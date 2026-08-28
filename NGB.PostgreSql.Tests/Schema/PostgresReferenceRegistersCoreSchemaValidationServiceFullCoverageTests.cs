using System.Data;
using System.Data.Common;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NGB.Metadata.Base;
using NGB.Metadata.Schema;
using NGB.Persistence.Schema;
using NGB.PostgreSql.Schema;
using NGB.PostgreSql.Tests.TestDoubles;
using NGB.ReferenceRegisters;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PostgreSql.Tests.Schema;

public sealed class PostgresReferenceRegistersCoreSchemaValidationServiceFullCoverageTests
{
    private static readonly RegisterSpec IndependentNonPeriodic = new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        "prices",
        ReferenceRegisterPeriodicity.NonPeriodic,
        ReferenceRegisterRecordMode.Independent);

    private static readonly RegisterSpec SubordinatePeriodic = new(
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        "daily_rates",
        ReferenceRegisterPeriodicity.Day,
        ReferenceRegisterRecordMode.SubordinateToRecorder);

    private static readonly RegisterSpec SubordinateNonPeriodic = new(
        Guid.Parse("33333333-3333-3333-3333-333333333333"),
        "links",
        ReferenceRegisterPeriodicity.NonPeriodic,
        ReferenceRegisterRecordMode.SubordinateToRecorder);

    private static readonly RegisterSpec IndependentPeriodic = new(
        Guid.Parse("44444444-4444-4444-4444-444444444444"),
        "monthly_balances",
        ReferenceRegisterPeriodicity.Month,
        ReferenceRegisterRecordMode.Independent);

    [Fact]
    public async Task Valid_schema_accepts_all_periodicity_record_mode_index_and_field_type_combinations()
    {
        var registers = new[]
        {
            IndependentNonPeriodic,
            SubordinatePeriodic,
            SubordinateNonPeriodic,
            IndependentPeriodic
        };
        var fields = AllValidFields(IndependentNonPeriodic.Id);
        var connection = ValidationConnection(
            registers,
            fields,
            ValidColumnMeta(fields),
            [
                "CHECK (recorder_document_id IS NULL)",
                "CHECK (period_utc IS NULL AND period_bucket_utc IS NULL)"
            ]);

        var act = async () => await Sut(ValidSnapshot(registers), connection).ValidateAsync(default);

        await act.Should().NotThrowAsync();
        connection.Commands.Should().Contain(command => command.CommandText.Contains("FROM reference_registers"));
        connection.Commands.Should().Contain(command => command.CommandText.Contains("information_schema.columns"));
        connection.Commands.Count(command => command.CommandText.Contains("pg_get_constraintdef", StringComparison.Ordinal))
            .Should().Be(1);
        connection.Commands.Count(command => command.CommandText.Contains("SELECT DISTINCT cl.relname", StringComparison.Ordinal))
            .Should().Be(1);
    }

    [Fact]
    public async Task Missing_core_contract_collects_all_errors_and_handles_no_physical_registers()
    {
        var empty = new DbSchemaSnapshot(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, IReadOnlyList<DbColumnSchema>>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, IReadOnlyList<DbForeignKeySchema>>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, IReadOnlyList<DbIndexSchema>>(StringComparer.OrdinalIgnoreCase));
        var connection = ValidationConnection([], [], [], [], coreObjectCount: 0, appendTriggerCount: 0);

        Func<Task> act = () => Sut(empty, connection).ValidateAsync(default);

        var error = await act.Should().ThrowAsync<NgbConfigurationViolationException>();
        error.Which.Message.Should()
            .Contain("Missing table 'reference_registers'")
            .And.Contain("Cannot read columns for table 'reference_registers'")
            .And.Contain("Missing index 'ux_reference_registers_code_norm'")
            .And.Contain("Missing foreign key")
            .And.Contain("Missing function")
            .And.Contain("Missing trigger");
    }

    [Fact]
    public async Task Physical_schema_drift_reports_missing_table_columns_constraints_trigger_indexes_and_field_metadata()
    {
        var missingTable = IndependentNonPeriodic with { Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), TableCode = "missing" };
        var missingColumns = IndependentPeriodic with { Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), TableCode = "no_columns" };
        var invalid = IndependentNonPeriodic with { Id = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), TableCode = "invalid" };
        var registers = new[] { missingTable, missingColumns, invalid };
        var fields = InvalidFields(invalid.Id);
        var snapshot = InvalidPhysicalSnapshot(missingTable, missingColumns, invalid);
        var meta = InvalidColumnMeta(fields);
        var connection = ValidationConnection(
            registers,
            fields,
            meta,
            ["CHECK (period_utc IS NULL)"],
            appendTriggerCount: 0);

        Func<Task> act = () => Sut(snapshot, connection).ValidateAsync(default);

        var error = await act.Should().ThrowAsync<NgbConfigurationViolationException>();
        error.Which.Message.Should()
            .Contain("physical table 'refreg_missing__records' is missing")
            .And.Contain("Cannot read columns for table 'refreg_no_columns__records'")
            .And.Contain("is missing required column")
            .And.Contain("has type 'text', expected 'bigint'")
            .And.Contain("nullability mismatch")
            .And.Contain("recorder_document_id IS NULL")
            .And.Contain("period_bucket_utc IS NULL")
            .And.Contain("missing append-only trigger")
            .And.Contain("missing key_v2 index")
            .And.Contain("missing field column 'missing_col'")
            .And.Contain("type mismatch");
    }

    [Fact]
    public async Task Unknown_persisted_column_type_fails_as_an_invariant_violation()
    {
        var invalid = IndependentNonPeriodic with
        {
            Id = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            TableCode = "unknown_type"
        };
        var field = new FieldSpec(invalid.Id, "unknown", "unknown_col", 999, true);
        var connection = ValidationConnection(
            [invalid],
            [field],
            [new ColumnMetaSpec("unknown_col", "YES", "text", null, null)],
            [
                "CHECK (recorder_document_id IS NULL)",
                "CHECK (period_utc IS NULL AND period_bucket_utc IS NULL)"
            ]);

        Func<Task> act = () => Sut(ValidSnapshot([invalid]), connection).ValidateAsync(default);

        var error = await act.Should().ThrowAsync<NgbInvariantViolationException>();
        error.Which.Message.Should().Contain("Unsupported ColumnType");
    }

    private static PostgresReferenceRegistersCoreSchemaValidationService Sut(
        DbSchemaSnapshot snapshot,
        RecordingDbConnection connection)
    {
        var inspector = new Mock<IDbSchemaInspector>(MockBehavior.Strict);
        inspector.Setup(x => x.GetSnapshotAsync(It.IsAny<CancellationToken>())).ReturnsAsync(snapshot);
        return new PostgresReferenceRegistersCoreSchemaValidationService(
            inspector.Object,
            new RecordingUnitOfWork(connection),
            NullLogger<PostgresReferenceRegistersCoreSchemaValidationService>.Instance);
    }

    private static RecordingDbConnection ValidationConnection(
        IReadOnlyList<RegisterSpec> registers,
        IReadOnlyList<FieldSpec> fields,
        IReadOnlyList<ColumnMetaSpec> meta,
        IReadOnlyList<string> constraintDefinitions,
        int coreObjectCount = 1,
        int appendTriggerCount = 1)
        => new(
            readerFactory: sql =>
            {
                if (sql.Contains("FROM reference_registers", StringComparison.Ordinal))
                    return RegisterRows(registers);

                if (sql.Contains("FROM reference_register_fields", StringComparison.Ordinal))
                    return FieldRows(fields);

                if (sql.Contains("pg_get_constraintdef", StringComparison.Ordinal))
                    return ConstraintRows(registers, constraintDefinitions);

                if (sql.Contains("SELECT DISTINCT cl.relname", StringComparison.Ordinal))
                {
                    return StringRows(appendTriggerCount == 0
                        ? []
                        : registers.Select(Table).ToArray());
                }

                if (sql.Contains("information_schema.columns", StringComparison.Ordinal))
                    return ColumnMetaRows(registers, fields, meta);

                return new DataTable().CreateDataReader();
            },
            scalar: _ => coreObjectCount);

    private static DbSchemaSnapshot ValidSnapshot(IReadOnlyList<RegisterSpec> registers)
    {
        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "reference_registers",
            "reference_register_fields",
            "reference_register_dimension_rules",
            "reference_register_write_state",
            "platform_dimensions",
            "platform_dimension_sets",
            "documents"
        };
        var columns = CoreColumns();
        var indexes = CoreIndexes();
        var foreignKeys = CoreForeignKeys();

        foreach (var register in registers)
        {
            var table = Table(register);
            tables.Add(table);
            columns[table] = PhysicalColumns(register);
            indexes[table] = PhysicalIndexes(register);
            foreignKeys[table] = PhysicalForeignKeys(table);
        }

        return new DbSchemaSnapshot(tables, columns, foreignKeys, indexes);
    }

    private static DbSchemaSnapshot InvalidPhysicalSnapshot(
        RegisterSpec missingTable,
        RegisterSpec missingColumns,
        RegisterSpec invalid)
    {
        var validCore = ValidSnapshot([]);
        var tables = new HashSet<string>(validCore.Tables, StringComparer.OrdinalIgnoreCase)
        {
            Table(missingColumns),
            Table(invalid)
        };
        var columns = validCore.ColumnsByTable.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        var invalidColumns = PhysicalColumns(invalid).ToList();
        invalidColumns.RemoveAll(column => column.ColumnName == "dimension_set_id");
        invalidColumns[0] = invalidColumns[0] with { DbType = "text", IsNullable = true };
        var periodIndex = invalidColumns.FindIndex(column => column.ColumnName == "period_utc");
        invalidColumns[periodIndex] = invalidColumns[periodIndex] with { IsNullable = false };
        columns[Table(invalid)] = invalidColumns;

        var indexes = validCore.IndexesByTable.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        indexes[Table(invalid)] =
        [
            new DbIndexSchema(Table(invalid), "too_short", ["dimension_set_id"], false),
            new DbIndexSchema(
                Table(invalid),
                "wrong_second",
                ["dimension_set_id", "wrong", "recorded_at_utc", "record_id"],
                false)
        ];

        var foreignKeys = validCore.ForeignKeysByTable
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);

        return new DbSchemaSnapshot(tables, columns, foreignKeys, indexes);
    }

    private static Dictionary<string, IReadOnlyList<DbColumnSchema>> CoreColumns()
        => new(StringComparer.OrdinalIgnoreCase)
        {
            ["reference_registers"] = Columns(
                "reference_registers",
                "register_id", "code", "code_norm", "name", "table_code", "periodicity", "record_mode", "has_records"),
            ["reference_register_fields"] = Columns(
                "reference_register_fields",
                "register_id", "code", "code_norm", "column_code", "name", "ordinal", "column_type", "is_nullable"),
            ["reference_register_dimension_rules"] = Columns(
                "reference_register_dimension_rules",
                "register_id", "dimension_id", "ordinal", "is_required"),
            ["reference_register_write_state"] = Columns(
                "reference_register_write_state",
                "register_id", "document_id", "operation", "started_at_utc", "completed_at_utc")
        };

    private static Dictionary<string, IReadOnlyList<DbIndexSchema>> CoreIndexes()
        => new(StringComparer.OrdinalIgnoreCase)
        {
            ["reference_registers"] =
            [
                Index("reference_registers", "ux_reference_registers_code_norm"),
                Index("reference_registers", "ux_reference_registers_table_code")
            ],
            ["reference_register_fields"] =
            [
                Index("reference_register_fields", "ix_refreg_fields_register_ordinal"),
                Index("reference_register_fields", "ux_reference_register_fields__register_code_norm"),
                Index("reference_register_fields", "ux_reference_register_fields__register_ordinal")
            ],
            ["reference_register_dimension_rules"] =
            [
                Index("reference_register_dimension_rules", "ix_refreg_dim_rules_register_ordinal"),
                Index("reference_register_dimension_rules", "ux_reference_register_dimension_rules__register_ordinal")
            ],
            ["reference_register_write_state"] =
            [
                Index("reference_register_write_state", "ix_refreg_write_log_document")
            ]
        };

    private static Dictionary<string, IReadOnlyList<DbForeignKeySchema>> CoreForeignKeys()
        => new(StringComparer.OrdinalIgnoreCase)
        {
            ["reference_register_fields"] =
            [
                ForeignKey("reference_register_fields", "register_id", "reference_registers", "register_id")
            ],
            ["reference_register_dimension_rules"] =
            [
                ForeignKey("reference_register_dimension_rules", "register_id", "reference_registers", "register_id"),
                ForeignKey("reference_register_dimension_rules", "dimension_id", "platform_dimensions", "dimension_id")
            ],
            ["reference_register_write_state"] =
            [
                ForeignKey("reference_register_write_state", "register_id", "reference_registers", "register_id"),
                ForeignKey("reference_register_write_state", "document_id", "documents", "id")
            ]
        };

    private static IReadOnlyList<DbColumnSchema> PhysicalColumns(RegisterSpec register)
    {
        var table = Table(register);
        var periodNullable = register.Periodicity == ReferenceRegisterPeriodicity.NonPeriodic;
        var recorderNullable = register.RecordMode != ReferenceRegisterRecordMode.SubordinateToRecorder;
        return
        [
            new DbColumnSchema(table, "record_id", "bigint", false, null),
            new DbColumnSchema(table, "dimension_set_id", "uuid", false, null),
            new DbColumnSchema(table, "period_utc", "timestamp with time zone", periodNullable, null),
            new DbColumnSchema(table, "period_bucket_utc", "timestamp with time zone", periodNullable, null),
            new DbColumnSchema(table, "recorder_document_id", "uuid", recorderNullable, null),
            new DbColumnSchema(table, "recorded_at_utc", "timestamp with time zone", false, null),
            new DbColumnSchema(table, "is_deleted", "boolean", false, null)
        ];
    }

    private static IReadOnlyList<DbIndexSchema> PhysicalIndexes(RegisterSpec register)
    {
        var table = Table(register);
        var result = new List<DbIndexSchema>();
        result.Add(new DbIndexSchema(
            table,
            "key",
            register.Periodicity == ReferenceRegisterPeriodicity.NonPeriodic
                ? ["dimension_set_id", "recorder_document_id", "recorded_at_utc", "record_id"]
                : ["dimension_set_id", "recorder_document_id", "period_bucket_utc", "period_utc", "recorded_at_utc", "record_id"],
            false));

        if (register.RecordMode == ReferenceRegisterRecordMode.SubordinateToRecorder)
        {
            result.Add(new DbIndexSchema(
                table,
                "recorder",
                register.Periodicity == ReferenceRegisterPeriodicity.NonPeriodic
                    ? ["recorder_document_id", "dimension_set_id", "recorded_at_utc", "record_id"]
                    : ["recorder_document_id", "dimension_set_id", "period_bucket_utc", "period_utc", "recorded_at_utc", "record_id"],
                false));
        }

        return result;
    }

    private static IReadOnlyList<DbForeignKeySchema> PhysicalForeignKeys(string table)
        =>
        [
            ForeignKey(table, "dimension_set_id", "platform_dimension_sets", "dimension_set_id"),
            ForeignKey(table, "recorder_document_id", "documents", "id")
        ];

    private static IReadOnlyList<FieldSpec> AllValidFields(Guid registerId)
        => Enum.GetValues<ColumnType>()
            .Select((type, index) => new FieldSpec(registerId, $"field_{index}", $"field_{index}", (short)type, index % 2 == 0))
            .ToArray();

    private static IReadOnlyList<FieldSpec> InvalidFields(Guid registerId)
    {
        var result = Enum.GetValues<ColumnType>()
            .Select((type, index) => new FieldSpec(
                registerId,
                $"bad_{type}",
                $"bad_{index}",
                (short)type,
                type != ColumnType.String))
            .ToList();
        result.Add(new FieldSpec(registerId, "missing", "missing_col", (short)ColumnType.String, false));
        result.Add(new FieldSpec(registerId, "decimal_null_precision", "decimal_null_precision", (short)ColumnType.Decimal, true));
        result.Add(new FieldSpec(registerId, "decimal_wrong_precision", "decimal_wrong_precision", (short)ColumnType.Decimal, true));
        result.Add(new FieldSpec(registerId, "decimal_null_scale", "decimal_null_scale", (short)ColumnType.Decimal, true));
        result.Add(new FieldSpec(registerId, "decimal_wrong_scale", "decimal_wrong_scale", (short)ColumnType.Decimal, true));
        return result;
    }

    private static IReadOnlyList<ColumnMetaSpec> ValidColumnMeta(IReadOnlyList<FieldSpec> fields)
        => fields.Select(field =>
        {
            var type = (ColumnType)field.ColumnType;
            return new ColumnMetaSpec(
                field.ColumnCode,
                field.IsNullable ? "YES" : "NO",
                Udt(type),
                type == ColumnType.Decimal ? 28 : null,
                type == ColumnType.Decimal ? 8 : null);
        }).ToArray();

    private static IReadOnlyList<ColumnMetaSpec> InvalidColumnMeta(IReadOnlyList<FieldSpec> fields)
    {
        var result = fields
            .Where(field => field.ColumnCode != "missing_col" && !field.ColumnCode.StartsWith("decimal_", StringComparison.Ordinal))
            .Select(field => new ColumnMetaSpec(
                field.ColumnCode,
                field.CodeNorm == "bad_String" ? "YES" : "NO",
                "wrong",
                1,
                1))
            .ToList();
        result.Add(new ColumnMetaSpec("decimal_null_precision", "YES", "numeric", null, 8));
        result.Add(new ColumnMetaSpec("decimal_wrong_precision", "YES", "numeric", 27, 8));
        result.Add(new ColumnMetaSpec("decimal_null_scale", "YES", "numeric", 28, null));
        result.Add(new ColumnMetaSpec("decimal_wrong_scale", "YES", "numeric", 28, 7));
        return result;
    }

    private static string Udt(ColumnType type) => type switch
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
        _ => throw new InvalidOperationException()
    };

    private static string Table(RegisterSpec register) => $"refreg_{register.TableCode}__records";

    private static IReadOnlyList<DbColumnSchema> Columns(string table, params string[] names)
        => names.Select(name => new DbColumnSchema(table, name, "text", true, null)).ToArray();

    private static DbIndexSchema Index(string table, string name)
        => new(table, name, [], true);

    private static DbForeignKeySchema ForeignKey(string table, string column, string target, string targetColumn)
        => new(table, $"fk_{table}_{column}", column, target, targetColumn);

    private static DbDataReader RegisterRows(IReadOnlyList<RegisterSpec> rows)
    {
        var table = new DataTable();
        table.Columns.Add("RegisterId", typeof(Guid));
        table.Columns.Add("TableCode", typeof(string));
        table.Columns.Add("Periodicity", typeof(short));
        table.Columns.Add("RecordMode", typeof(short));
        table.Columns.Add("HasRecords", typeof(bool));
        foreach (var row in rows)
            table.Rows.Add(row.Id, row.TableCode, (short)row.Periodicity, (short)row.RecordMode, true);

        return table.CreateDataReader();
    }

    private static DbDataReader FieldRows(IReadOnlyList<FieldSpec> rows)
    {
        var table = new DataTable();
        table.Columns.Add("RegisterId", typeof(Guid));
        table.Columns.Add("CodeNorm", typeof(string));
        table.Columns.Add("ColumnCode", typeof(string));
        table.Columns.Add("ColumnType", typeof(short));
        table.Columns.Add("IsNullable", typeof(bool));
        foreach (var row in rows)
            table.Rows.Add(row.RegisterId, row.CodeNorm, row.ColumnCode, row.ColumnType, row.IsNullable);

        return table.CreateDataReader();
    }

    private static DbDataReader ColumnMetaRows(
        IReadOnlyList<RegisterSpec> registers,
        IReadOnlyList<FieldSpec> fields,
        IReadOnlyList<ColumnMetaSpec> rows)
    {
        var table = new DataTable();
        table.Columns.Add("TableName", typeof(string));
        table.Columns.Add("ColumnName", typeof(string));
        table.Columns.Add("IsNullable", typeof(string));
        table.Columns.Add("UdtName", typeof(string));
        table.Columns.Add("NumericPrecision", typeof(int));
        table.Columns.Add("NumericScale", typeof(int));
        foreach (var row in rows)
        {
            var field = fields.First(x => x.ColumnCode == row.ColumnName);
            var register = registers.First(x => x.Id == field.RegisterId);
            table.Rows.Add(
                Table(register),
                row.ColumnName,
                row.IsNullable,
                row.UdtName,
                row.NumericPrecision ?? (object)DBNull.Value,
                row.NumericScale ?? (object)DBNull.Value);
        }

        return table.CreateDataReader();
    }

    private static DbDataReader ConstraintRows(IReadOnlyList<RegisterSpec> registers, IReadOnlyList<string> definitions)
    {
        var table = new DataTable();
        table.Columns.Add("TableName", typeof(string));
        table.Columns.Add("Definition", typeof(string));

        foreach (var register in registers)
        {
            foreach (var definition in definitions)
            {
                table.Rows.Add(Table(register), definition);
            }
        }

        return table.CreateDataReader();
    }

    private static DbDataReader StringRows(IReadOnlyList<string> rows)
    {
        var table = new DataTable();
        table.Columns.Add("Definition", typeof(string));

        foreach (var row in rows)
        {
            table.Rows.Add(row);
        }

        return table.CreateDataReader();
    }

    private sealed record RegisterSpec(
        Guid Id,
        string TableCode,
        ReferenceRegisterPeriodicity Periodicity,
        ReferenceRegisterRecordMode RecordMode);

    private sealed record FieldSpec(
        Guid RegisterId,
        string CodeNorm,
        string ColumnCode,
        short ColumnType,
        bool IsNullable);

    private sealed record ColumnMetaSpec(
        string ColumnName,
        string IsNullable,
        string UdtName,
        int? NumericPrecision,
        int? NumericScale);
}
