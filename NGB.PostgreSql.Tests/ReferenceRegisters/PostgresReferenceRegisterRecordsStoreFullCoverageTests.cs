using System.Collections;
using System.Data;
using System.Data.Common;
using FluentAssertions;
using Moq;
using NGB.Metadata.Base;
using NGB.Persistence.ReferenceRegisters;
using NGB.PostgreSql.ReferenceRegisters;
using NGB.PostgreSql.Tests.TestDoubles;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.ReferenceRegisters.Exceptions;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PostgreSql.Tests.ReferenceRegisters;

public sealed class PostgresReferenceRegisterRecordsStoreFullCoverageTests
{
    private static readonly Guid RegisterId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid RecorderId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTime Utc = new(2026, 8, 16, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Public_operations_validate_required_inputs_empty_batches_and_missing_metadata()
    {
        var fixture = Fixture(Reg(), []);
        Func<Task> emptyRegister = () => fixture.Store.EnsureSchemaAsync(Guid.Empty, default);
        Func<Task> nullRecords = () => fixture.Store.AppendAsync(RegisterId, null!, default);
        await emptyRegister.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await nullRecords.Should().ThrowAsync<NgbArgumentRequiredException>();
        await fixture.Store.AppendAsync(RegisterId, [], default);
        fixture.Connection.Commands.Should().BeEmpty();

        var missing = Fixture(Reg(), []);
        missing.Registers.Setup(x => x.GetByIdAsync(RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReferenceRegisterAdminItem?)null);
        Func<Task> ensureMissing = () => missing.Store.EnsureSchemaAsync(RegisterId, default);
        await ensureMissing.Should().ThrowAsync<ReferenceRegisterNotFoundException>();

        var appendMissing = Fixture(Reg(), []);
        appendMissing.Registers.SetupSequence(x => x.GetByIdAsync(RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Reg())
            .ReturnsAsync((ReferenceRegisterAdminItem?)null);
        Func<Task> append = () => appendMissing.Store.AppendAsync(RegisterId, [Write()], default);
        await append.Should().ThrowAsync<ReferenceRegisterNotFoundException>();

        var tombstoneMissing = Fixture(Reg(mode: ReferenceRegisterRecordMode.SubordinateToRecorder), []);
        tombstoneMissing.Registers.SetupSequence(x => x.GetByIdAsync(RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Reg(mode: ReferenceRegisterRecordMode.SubordinateToRecorder))
            .ReturnsAsync((ReferenceRegisterAdminItem?)null);
        Func<Task> tombstone = () => tombstoneMissing.Store.AppendTombstonesForRecorderAsync(
            RegisterId, RecorderId, null, default);
        await tombstone.Should().ThrowAsync<ReferenceRegisterNotFoundException>();
    }

    [Fact]
    public async Task Append_rejects_null_values_and_invalid_recorder_period_and_field_keys()
    {
        await AssertInvalidAsync(
            Reg(),
            new ReferenceRegisterRecordWrite(Guid.NewGuid(), null, null, null!),
            "values_null");
        await AssertInvalidAsync(
            Reg(mode: ReferenceRegisterRecordMode.SubordinateToRecorder),
            Write(recorder: null),
            "recorder_required");
        await AssertInvalidAsync(
            Reg(mode: ReferenceRegisterRecordMode.SubordinateToRecorder),
            Write(recorder: Guid.Empty),
            "recorder_required");
        await AssertInvalidAsync(Reg(), Write(recorder: RecorderId), "recorder_forbidden");
        await AssertInvalidAsync(Reg(), Write(period: Utc), "period_not_allowed_for_non_periodic");
        await AssertInvalidAsync(
            Reg(periodicity: ReferenceRegisterPeriodicity.Day),
            Write(period: null),
            "period_required_for_periodic");
        await AssertInvalidAsync(
            Reg(periodicity: ReferenceRegisterPeriodicity.Day),
            Write(period: DateTime.SpecifyKind(Utc, DateTimeKind.Local)),
            "period_not_utc");
        await AssertInvalidAsync(Reg(), Write(values: new NullKeyDictionary()), "field_key_null");
        await AssertInvalidAsync(Reg(), Write(values: new Dictionary<string, object?> { [" "] = 1 }), "field_key_empty");
        await AssertInvalidAsync(Reg(), Write(values: new Dictionary<string, object?> { ["unknown"] = 1 }), "unknown_field");
    }

    [Fact]
    public async Task Append_rejects_missing_required_field_and_non_utc_datetime_field()
    {
        var required = Field("required", "required_col", ColumnType.String, nullable: false);
        var missing = Fixture(Reg(), [required]);
        Func<Task> missingValue = () => missing.Store.AppendAsync(RegisterId, [Write()], default);
        var missingError = await missingValue.Should().ThrowAsync<ReferenceRegisterRecordsValidationException>();
        missingError.Which.Reason.Should().Be("missing_not_null_field");

        var timestamp = Field("timestamp", "timestamp_col", ColumnType.DateTimeUtc, nullable: true);
        var local = Fixture(Reg(), [timestamp]);
        Func<Task> localValue = () => local.Store.AppendAsync(
            RegisterId,
            [Write(values: new Dictionary<string, object?>
            {
                ["timestamp"] = DateTime.SpecifyKind(Utc, DateTimeKind.Unspecified)
            })],
            default);
        var timeError = await localValue.Should().ThrowAsync<ReferenceRegisterRecordsValidationException>();
        timeError.Which.Reason.Should().Be("datetime_not_utc");
    }

    [Fact]
    public async Task Append_batches_records_maps_periods_and_emits_json_casts()
    {
        var fields = new[]
        {
            Field("timestamp", "timestamp_col", ColumnType.DateTimeUtc, nullable: false),
            Field("payload", "payload_col", ColumnType.Json, nullable: true),
            Field("note", "note_col", ColumnType.String, nullable: true)
        };
        var meta = new[]
        {
            Meta("timestamp_col", "NO", "timestamptz"),
            Meta("payload_col", "YES", "jsonb"),
            Meta("note_col", "YES", "text")
        };
        var fixture = Fixture(
            Reg(ReferenceRegisterPeriodicity.Day, ReferenceRegisterRecordMode.SubordinateToRecorder),
            fields,
            meta);
        var values = new Dictionary<string, object?>
        {
            ["timestamp"] = Utc,
            ["payload"] = "{\"ok\":true}",
            ["note"] = null
        };
        var records = Enumerable.Range(0, 501)
            .Select(index => Write(
                dimensionSetId: index == 0 ? Guid.Empty : Guid.NewGuid(),
                period: Utc.AddDays(index),
                recorder: RecorderId,
                values: values,
                deleted: index % 2 == 0))
            .ToArray();

        await fixture.Store.AppendAsync(RegisterId, records, default);

        var inserts = fixture.Connection.Commands
            .Where(command => command.CommandText.StartsWith("INSERT INTO refreg_prices__records", StringComparison.Ordinal))
            .ToArray();
        inserts.Should().HaveCount(2);
        inserts[0].CommandText.Should().Contain("::jsonb").And.Contain(",\n");
        inserts[1].ParametersSnapshot.Should().Contain(parameter =>
            parameter.ParameterName == "PeriodBucketUtc_0" && parameter.Value is DateTime);
    }

    [Fact]
    public async Task Repeated_appends_in_the_same_transaction_reuse_successful_schema_repair()
    {
        var fixture = Fixture(Reg(), []);
        var store = fixture.Store;

        await store.AppendAsync(RegisterId, [Write()], default);
        var schemaCommandCount = fixture.Connection.Commands.Count(command =>
            command.CommandText.Contains("CREATE TABLE IF NOT EXISTS refreg_prices__records", StringComparison.Ordinal));

        await store.AppendAsync(RegisterId, [Write()], default);

        fixture.Connection.Commands.Count(command =>
                command.CommandText.Contains("CREATE TABLE IF NOT EXISTS refreg_prices__records", StringComparison.Ordinal))
            .Should().Be(schemaCommandCount).And.Be(1);
        fixture.Connection.Commands.Count(command =>
                command.CommandText.StartsWith("INSERT INTO refreg_prices__records", StringComparison.Ordinal))
            .Should().Be(2);
    }

    [Fact]
    public async Task Tombstones_cover_independent_periodic_nonperiodic_fields_and_keep_filters()
    {
        var independent = Fixture(Reg(), []);
        await independent.Store.AppendTombstonesForRecorderAsync(RegisterId, RecorderId, null, default);
        independent.Connection.Commands.Should().NotContain(command => command.CommandText.Contains("WITH last_rows"));

        var nonPeriodic = Fixture(Reg(mode: ReferenceRegisterRecordMode.SubordinateToRecorder), []);
        await nonPeriodic.Store.AppendTombstonesForRecorderAsync(RegisterId, RecorderId, null, default);
        await nonPeriodic.Store.AppendTombstonesForRecorderAsync(RegisterId, RecorderId, [], default);
        var nonPeriodicSql = nonPeriodic.Connection.Commands.Last().CommandText;
        nonPeriodicSql.Should().Contain("DISTINCT ON (t.dimension_set_id, t.recorder_document_id)")
            .And.NotContain("KeepDimensionSetIds");

        var field = Field("payload", "payload_col", ColumnType.Json, nullable: true);
        var periodic = Fixture(
            Reg(ReferenceRegisterPeriodicity.Month, ReferenceRegisterRecordMode.SubordinateToRecorder),
            [field],
            [Meta("payload_col", "YES", "jsonb")]);
        var keep = Guid.Parse("33333333-3333-3333-3333-333333333333");
        await periodic.Store.AppendTombstonesForRecorderAsync(
            RegisterId, RecorderId, [keep, keep, Guid.Empty], default);

        var periodicCommand = periodic.Connection.Commands.Last();
        periodicCommand.CommandText.Should()
            .Contain("period_bucket_utc, t.period_utc")
            .And.Contain("t.payload_col")
            .And.Contain("AND NOT (dimension_set_id = ANY(");
        periodicCommand.ParametersSnapshot
            .Where(parameter => parameter.ParameterName.StartsWith("KeepDimensionSetIds", StringComparison.Ordinal))
            .Select(parameter => parameter.Value)
            .Should().Equal(keep, Guid.Empty);
    }

    [Fact]
    public async Task Ensure_schema_repairs_missing_type_and_nullability_drift_for_all_supported_types()
    {
        var fields = new[]
        {
            Field("missing_nullable", "missing_nullable", ColumnType.String, true),
            Field("missing_required", "missing_required", ColumnType.Int32, false),
            Field("int64", "int64_col", ColumnType.Int64, true),
            Field("decimal_null_precision", "decimal_null_precision", ColumnType.Decimal, true),
            Field("decimal_wrong_precision", "decimal_wrong_precision", ColumnType.Decimal, true),
            Field("decimal_null_scale", "decimal_null_scale", ColumnType.Decimal, true),
            Field("decimal_wrong_scale", "decimal_wrong_scale", ColumnType.Decimal, true),
            Field("decimal_valid", "decimal_valid", ColumnType.Decimal, true),
            Field("boolean", "boolean_col", ColumnType.Boolean, true),
            Field("guid", "guid_col", ColumnType.Guid, false),
            Field("date", "date_col", ColumnType.Date, true),
            Field("timestamp", "timestamp_col", ColumnType.DateTimeUtc, true),
            Field("json", "json_col", ColumnType.Json, true),
            Field("string", "string_col", ColumnType.String, true)
        };
        var meta = new[]
        {
            Meta("int64_col", "YES", "wrong"),
            Meta("decimal_null_precision", "YES", "numeric", null, 8),
            Meta("decimal_wrong_precision", "YES", "numeric", 27, 8),
            Meta("decimal_null_scale", "YES", "numeric", 28, null),
            Meta("decimal_wrong_scale", "YES", "numeric", 28, 7),
            Meta("decimal_valid", "YES", "numeric", 28, 8),
            Meta("boolean_col", "NO", "bool"),
            Meta("guid_col", "YES", "uuid"),
            Meta("date_col", "YES", "date"),
            Meta("timestamp_col", "YES", "timestamptz"),
            Meta("json_col", "YES", "jsonb"),
            Meta("string_col", "YES", "text")
        };
        var fixture = Fixture(Reg(), fields, meta);

        await fixture.Store.EnsureSchemaAsync(RegisterId, default);

        var sql = string.Join("\n", fixture.Connection.Commands.Select(command => command.CommandText));
        sql.Should()
            .Contain("ADD COLUMN IF NOT EXISTS missing_nullable TEXT NULL")
            .And.Contain("ADD COLUMN IF NOT EXISTS missing_required INTEGER NOT NULL")
            .And.Contain("ALTER COLUMN int64_col TYPE BIGINT")
            .And.Contain("ALTER COLUMN boolean_col DROP NOT NULL")
            .And.Contain("ALTER COLUMN guid_col SET NOT NULL");
    }

    [Theory]
    [InlineData("missing_required", "missing_required", (short)ColumnType.String, false, null, null, null, "missing_not_null_column")]
    [InlineData("type", "type_col", (short)ColumnType.String, true, "YES", "int4", null, "type_mismatch")]
    [InlineData("nullable", "nullable_col", (short)ColumnType.String, true, "NO", "text", null, "nullability_mismatch")]
    [InlineData("required", "required_col", (short)ColumnType.String, false, "YES", "text", null, "nullability_mismatch")]
    public async Task Ensure_schema_refuses_unsafe_drift_after_records_exist(
        string code,
        string column,
        short type,
        bool nullable,
        string? actualNullable,
        string? udt,
        int? precision,
        string expectedReason)
    {
        var field = Field(code, column, (ColumnType)type, nullable);
        var meta = actualNullable is null ? [] : new[] { Meta(column, actualNullable, udt!, precision, null) };
        var fixture = Fixture(Reg(hasRecords: true), [field], meta);

        Func<Task> act = () => fixture.Store.EnsureSchemaAsync(RegisterId, default);

        var error = await act.Should().ThrowAsync<ReferenceRegisterSchemaDriftAfterRecordsExistException>();
        error.Which.Reason.Should().Be(expectedReason);
    }

    [Fact]
    public async Task Existing_nullable_field_can_be_added_after_records_and_unknown_type_is_an_invariant_error()
    {
        var nullable = Fixture(
            Reg(hasRecords: true),
            [Field("optional", "optional_col", ColumnType.String, true)]);
        await nullable.Store.EnsureSchemaAsync(RegisterId, default);
        nullable.Connection.Commands.Should().Contain(command =>
            command.CommandText.Contains("ADD COLUMN IF NOT EXISTS optional_col TEXT NULL", StringComparison.Ordinal));

        var unknown = Fixture(
            Reg(),
            [Field("unknown", "unknown_col", (ColumnType)999, true)]);
        Func<Task> act = () => unknown.Store.EnsureSchemaAsync(RegisterId, default);
        await act.Should().ThrowAsync<NgbInvariantViolationException>();
    }

    private static async Task AssertInvalidAsync(
        ReferenceRegisterAdminItem register,
        ReferenceRegisterRecordWrite write,
        string expectedReason)
    {
        var fixture = Fixture(register, []);
        Func<Task> act = () => fixture.Store.AppendAsync(RegisterId, [write], default);
        var error = await act.Should().ThrowAsync<ReferenceRegisterRecordsValidationException>();
        error.Which.Reason.Should().Be(expectedReason);
    }

    private static StoreFixture Fixture(
        ReferenceRegisterAdminItem register,
        IReadOnlyList<ReferenceRegisterField> fields,
        IReadOnlyList<ColumnMetaSpec>? meta = null)
        => new(register, fields, meta ?? []);

    private static ReferenceRegisterAdminItem Reg(
        ReferenceRegisterPeriodicity periodicity = ReferenceRegisterPeriodicity.NonPeriodic,
        ReferenceRegisterRecordMode mode = ReferenceRegisterRecordMode.Independent,
        bool hasRecords = false)
        => new(RegisterId, "Prices", "prices", "prices", "Prices", periodicity, mode, hasRecords, Utc, Utc);

    private static ReferenceRegisterField Field(
        string code,
        string column,
        ColumnType type,
        bool nullable)
        => new(RegisterId, code, code, column, code, 0, type, nullable, Utc, Utc);

    private static ReferenceRegisterRecordWrite Write(
        Guid? dimensionSetId = null,
        DateTime? period = null,
        Guid? recorder = null,
        IReadOnlyDictionary<string, object?>? values = null,
        bool deleted = false)
        => new(dimensionSetId ?? Guid.NewGuid(), period, recorder, values ?? new Dictionary<string, object?>(), deleted);

    private static ColumnMetaSpec Meta(
        string column,
        string nullable,
        string udt,
        int? precision = null,
        int? scale = null)
        => new(column, nullable, udt, precision, scale);

    private sealed class StoreFixture(
        ReferenceRegisterAdminItem register,
        IReadOnlyList<ReferenceRegisterField> fields,
        IReadOnlyList<ColumnMetaSpec> meta)
    {
        public Mock<IReferenceRegisterRepository> Registers { get; } = CreateRegisters(register);
        public Mock<IReferenceRegisterFieldRepository> Fields { get; } = CreateFields(fields);
        public RecordingDbConnection Connection { get; } = new(
            readerFactory: sql => sql.Contains("information_schema.columns", StringComparison.Ordinal)
                ? ColumnMetaRows(meta)
                : new DataTable().CreateDataReader());

        public PostgresReferenceRegisterRecordsStore Store => new(
            new RecordingUnitOfWork(Connection, hasActiveTransaction: true),
            Registers.Object,
            Fields.Object);

        private static Mock<IReferenceRegisterRepository> CreateRegisters(ReferenceRegisterAdminItem register)
        {
            var mock = new Mock<IReferenceRegisterRepository>(MockBehavior.Loose);
            mock.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(register);
            return mock;
        }

        private static Mock<IReferenceRegisterFieldRepository> CreateFields(IReadOnlyList<ReferenceRegisterField> fields)
        {
            var mock = new Mock<IReferenceRegisterFieldRepository>(MockBehavior.Loose);
            mock.Setup(x => x.GetByRegisterIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(fields);
            return mock;
        }
    }

    private static DbDataReader ColumnMetaRows(IReadOnlyList<ColumnMetaSpec> rows)
    {
        var table = new DataTable();
        table.Columns.Add("ColumnName", typeof(string));
        table.Columns.Add("IsNullable", typeof(string));
        table.Columns.Add("UdtName", typeof(string));
        table.Columns.Add("NumericPrecision", typeof(int));
        table.Columns.Add("NumericScale", typeof(int));
        foreach (var row in rows)
        {
            table.Rows.Add(
                row.ColumnName,
                row.IsNullable,
                row.UdtName,
                row.NumericPrecision ?? (object)DBNull.Value,
                row.NumericScale ?? (object)DBNull.Value);
        }

        return table.CreateDataReader();
    }

    private sealed record ColumnMetaSpec(
        string ColumnName,
        string IsNullable,
        string UdtName,
        int? NumericPrecision,
        int? NumericScale);

    private sealed class NullKeyDictionary : IReadOnlyDictionary<string, object?>
    {
        public int Count => 1;
        public IEnumerable<string> Keys => [null!];
        public IEnumerable<object?> Values => [1];
        public object? this[string key] => 1;
        public bool ContainsKey(string key) => false;
        public bool TryGetValue(string key, out object? value)
        {
            value = null;
            return false;
        }

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
        {
            yield return new KeyValuePair<string, object?>(null!, 1);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
