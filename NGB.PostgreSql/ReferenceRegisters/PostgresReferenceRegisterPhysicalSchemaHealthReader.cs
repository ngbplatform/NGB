using Dapper;
using NGB.Metadata.Schema;
using NGB.Persistence.ReferenceRegisters;
using NGB.Persistence.Schema;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Internal;
using NGB.PostgreSql.ReferenceRegisters.Internal;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.Tools.Extensions;

namespace NGB.PostgreSql.ReferenceRegisters;

/// <summary>
/// PostgreSQL implementation of <see cref="IReferenceRegisterPhysicalSchemaHealthReader"/>.
///
/// This reader checks the *dynamic* per-register table (refreg_&lt;table_code&gt;__records)
/// against the expected physical contract derived from register metadata.
/// </summary>
public sealed class PostgresReferenceRegisterPhysicalSchemaHealthReader(
    IDbSchemaInspector schemaInspector,
    IUnitOfWork uow)
    : IReferenceRegisterPhysicalSchemaHealthReader
{
    public async Task<ReferenceRegisterPhysicalSchemaHealthReport> GetReportAsync(CancellationToken ct = default)
    {
        await uow.EnsureConnectionOpenAsync(ct);

        var registers = (await uow.Connection.QueryAsync<ReferenceRegisterRow>(
            new CommandDefinition(
                """
                SELECT
                    register_id     AS "RegisterId",
                    code            AS "Code",
                    code_norm       AS "CodeNorm",
                    table_code      AS "TableCode",
                    name            AS "Name",
                    periodicity     AS "Periodicity",
                    record_mode     AS "RecordMode",
                    has_records     AS "HasRecords",
                    created_at_utc  AS "CreatedAtUtc",
                    updated_at_utc  AS "UpdatedAtUtc"
                FROM reference_registers
                ORDER BY code_norm;
                """,
                transaction: uow.Transaction,
                cancellationToken: ct))).AsList();

        if (registers.Count == 0)
            return new ReferenceRegisterPhysicalSchemaHealthReport([]);

        var fieldRows = (await uow.Connection.QueryAsync<FieldRow>(
            new CommandDefinition(
                """
                SELECT
                    register_id AS "RegisterId",
                    column_code AS "ColumnCode"
                FROM reference_register_fields;
                """,
                transaction: uow.Transaction,
                cancellationToken: ct))).AsList();

        var expectedFieldColsByRegister = fieldRows
            .GroupBy(r => r.RegisterId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.ColumnCode)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(x => x)
                    .ToArray());

        var snapshot = await schemaInspector.GetSnapshotAsync(ct);

        var tables = registers
            .Select(r => ReferenceRegisterNaming.RecordsTable(r.TableCode))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        foreach (var t in tables)
        {
            ReferenceRegisterSqlIdentifiers.EnsureOrThrow(t, "refreg records table name");
        }

        var appendOnlyGuards = await PostgresPhysicalSchemaHealthHelpers.LoadAppendOnlyGuardPresenceAsync(uow, tables, ct);

        var result = new List<ReferenceRegisterPhysicalSchemaHealth>(registers.Count);

        foreach (var reg in registers)
        {
            var expectedFieldCols = expectedFieldColsByRegister.TryGetValue(reg.RegisterId, out var cols)
                ? cols
                : [];

            var table = ReferenceRegisterNaming.RecordsTable(reg.TableCode);
            ReferenceRegisterSqlIdentifiers.EnsureOrThrow(table, "refreg records table name");

            var records = BuildRecordsHealth(snapshot, table, reg, expectedFieldCols, appendOnlyGuards);
            result.Add(new ReferenceRegisterPhysicalSchemaHealth(reg.ToItem(), records));
        }

        return new ReferenceRegisterPhysicalSchemaHealthReport(result);
    }

    public async Task<ReferenceRegisterPhysicalSchemaHealth?> GetByRegisterIdAsync(
        Guid registerId,
        CancellationToken ct = default)
    {
        registerId.EnsureNonEmpty(nameof(registerId));
        var report = await GetReportAsync(ct);
        return report.Items.FirstOrDefault(x => x.Register.RegisterId == registerId);
    }

    private static ReferenceRegisterPhysicalTableHealth BuildRecordsHealth(
        DbSchemaSnapshot snapshot,
        string tableName,
        ReferenceRegisterRow reg,
        string[] expectedFieldCols,
        IReadOnlyDictionary<string, bool> appendOnlyGuards)
    {
        var requiredCols = new List<string>
        {
            "record_id",
            "dimension_set_id",
            "period_utc",
            "period_bucket_utc",
            "recorder_document_id",
            "recorded_at_utc",
            "is_deleted"
        };
        requiredCols.AddRange(expectedFieldCols);

        var requiredIndexes = BuildRequiredIndexes(reg);

        var diff = PostgresPhysicalSchemaHealthHelpers.ComputeTableDiff(snapshot, tableName, requiredCols, requiredIndexes);

        bool? hasGuard = null;
        if (diff.Exists)
            hasGuard = appendOnlyGuards.TryGetValue(tableName, out var v) && v;

        return new ReferenceRegisterPhysicalTableHealth(
            TableName: tableName,
            Exists: diff.Exists,
            MissingColumns: diff.MissingColumns,
            MissingIndexes: diff.MissingIndexes,
            HasAppendOnlyGuard: hasGuard);
    }

    private static (string[] Columns, bool UniqueRequired, string Label)[] BuildRequiredIndexes(ReferenceRegisterRow reg)
    {
        // NOTE: DbSchemaInspector does not retain sort direction; we check the column list shape only.

        var list = new List<(string[] Columns, bool UniqueRequired, string Label)>(capacity: 4);

        // 1) Key v2.
        if (reg.PeriodicityEnum == ReferenceRegisterPeriodicity.NonPeriodic)
        {
            list.Add((
                ["dimension_set_id", "recorder_document_id", "recorded_at_utc", "record_id"],
                false,
                "index(dimension_set_id, recorder_document_id, recorded_at_utc, record_id)"));
        }
        else
        {
            list.Add((
                ["dimension_set_id", "recorder_document_id", "period_bucket_utc", "period_utc", "recorded_at_utc", "record_id"],
                false,
                "index(dimension_set_id, recorder_document_id, period_bucket_utc, period_utc, recorded_at_utc, record_id)"));
        }

        // 2) Recorder scan index (only for SubordinateToRecorder).
        if (reg.RecordModeEnum == ReferenceRegisterRecordMode.SubordinateToRecorder)
        {
            if (reg.PeriodicityEnum == ReferenceRegisterPeriodicity.NonPeriodic)
            {
                list.Add((
                    ["recorder_document_id", "dimension_set_id", "recorded_at_utc", "record_id"],
                    false,
                    "index(recorder_document_id, dimension_set_id, recorded_at_utc, record_id)"));
            }
            else
            {
                list.Add((
                    ["recorder_document_id", "dimension_set_id", "period_bucket_utc", "period_utc", "recorded_at_utc", "record_id"],
                    false,
                    "index(recorder_document_id, dimension_set_id, period_bucket_utc, period_utc, recorded_at_utc, record_id)"));
            }
        }

        return list.ToArray();
    }

    private sealed class FieldRow
    {
        public Guid RegisterId { get; init; }
        public string ColumnCode { get; init; } = null!;
    }
}
