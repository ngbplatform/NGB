using Dapper;
using NGB.Metadata.Schema;
using NGB.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.Schema;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Internal;
using NGB.PostgreSql.OperationalRegisters.Internal;
using NGB.Tools.Extensions;

namespace NGB.PostgreSql.OperationalRegisters;

/// <summary>
/// PostgreSQL implementation of <see cref="IOperationalRegisterPhysicalSchemaHealthReader"/>.
///
/// This reader checks the *dynamic* per-register tables (opreg_&lt;table_code&gt;__*)
/// against the expected physical contract derived from register metadata.
/// </summary>
public sealed class PostgresOperationalRegisterPhysicalSchemaHealthReader(
    IDbSchemaInspector schemaInspector,
    IUnitOfWork uow)
    : IOperationalRegisterPhysicalSchemaHealthReader
{
    public async Task<OperationalRegisterPhysicalSchemaHealthReport> GetReportAsync(CancellationToken ct = default)
    {
        await uow.EnsureConnectionOpenAsync(ct);

        var registers = (await uow.Connection.QueryAsync<OperationalRegisterAdminItemRow>(
            new CommandDefinition(
                """
                SELECT
                    register_id     AS "RegisterId",
                    code            AS "Code",
                    code_norm       AS "CodeNorm",
                    table_code      AS "TableCode",
                    name            AS "Name",
                    has_movements   AS "HasMovements",
                    created_at_utc  AS "CreatedAtUtc",
                    updated_at_utc  AS "UpdatedAtUtc"
                FROM operational_registers
                ORDER BY code_norm;
                """,
                transaction: uow.Transaction,
                cancellationToken: ct))).AsList();

        if (registers.Count == 0)
            return new OperationalRegisterPhysicalSchemaHealthReport([]);

        // Load all resources once and group by RegisterId.
        var resourceRows = (await uow.Connection.QueryAsync<ResourceRow>(
            new CommandDefinition(
                """
                SELECT
                    register_id AS "RegisterId",
                    column_code AS "ColumnCode"
                FROM operational_register_resources;
                """,
                transaction: uow.Transaction,
                cancellationToken: ct))).AsList();

        var resourceColumnsByRegister = resourceRows
            .GroupBy(r => r.RegisterId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.ColumnCode)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(x => x)
                    .ToArray()
            );

        // Snapshot (tables/columns/indexes) for the whole schema.
        var snapshot = await schemaInspector.GetSnapshotAsync(ct);

        // Append-only guard presence for movements tables.
        var movementTables = registers
            .Select(r => OperationalRegisterNaming.MovementsTable(r.TableCode))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        foreach (var t in movementTables)
        {
            OperationalRegisterSqlIdentifiers.EnsureOrThrow(t, "opreg movements table name");
        }

        var appendOnlyGuards = await PostgresPhysicalSchemaHealthHelpers.LoadAppendOnlyGuardPresenceAsync(uow, movementTables, ct);

        var result = new List<OperationalRegisterPhysicalSchemaHealth>(registers.Count);

        foreach (var reg in registers)
        {
            var expectedResourceCols = resourceColumnsByRegister.TryGetValue(reg.RegisterId, out var cols)
                ? cols
                : [];

            var movementsTable = OperationalRegisterNaming.MovementsTable(reg.TableCode);
            var turnoversTable = OperationalRegisterNaming.TurnoversTable(reg.TableCode);
            var balancesTable = OperationalRegisterNaming.BalancesTable(reg.TableCode);

            OperationalRegisterSqlIdentifiers.EnsureOrThrow(movementsTable, "opreg movements table name");
            OperationalRegisterSqlIdentifiers.EnsureOrThrow(turnoversTable, "opreg turnovers table name");
            OperationalRegisterSqlIdentifiers.EnsureOrThrow(balancesTable, "opreg balances table name");

            var movements = BuildMovementsHealth(snapshot, movementsTable, expectedResourceCols, appendOnlyGuards);
            var turnovers = BuildDerivedTableHealth(snapshot, turnoversTable, expectedResourceCols);
            var balances = BuildDerivedTableHealth(snapshot, balancesTable, expectedResourceCols);

            result.Add(new OperationalRegisterPhysicalSchemaHealth(
                Register: reg.ToItem(),
                Movements: movements,
                Turnovers: turnovers,
                Balances: balances));
        }

        return new OperationalRegisterPhysicalSchemaHealthReport(result);
    }

    public async Task<OperationalRegisterPhysicalSchemaHealth?> GetByRegisterIdAsync(
        Guid registerId,
        CancellationToken ct = default)
    {
        registerId.EnsureNonEmpty(nameof(registerId));
        var report = await GetReportAsync(ct);
        return report.Items.FirstOrDefault(x => x.Register.RegisterId == registerId);
    }

    private static OperationalRegisterPhysicalTableHealth BuildMovementsHealth(
        DbSchemaSnapshot snapshot,
        string tableName,
        string[] expectedResourceCols,
        IReadOnlyDictionary<string, bool> appendOnlyGuards)
    {
        var requiredCols = new List<string>
        {
            "movement_id",
            "document_id",
            "occurred_at_utc",
            "period_month",
            "dimension_set_id",
            "is_storno"
        };
        requiredCols.AddRange(expectedResourceCols);

        var requiredIndexes = new[]
        {
            (Columns: ["document_id"], UniqueRequired: false, Label: "index(document_id)"),
            (Columns: ["period_month"], UniqueRequired: false, Label: "index(period_month)"),
            (Columns: new[] { "period_month", "dimension_set_id" }, UniqueRequired: false, Label: "index(period_month, dimension_set_id)"),
        };

        var diff = PostgresPhysicalSchemaHealthHelpers.ComputeTableDiff(snapshot, tableName, requiredCols, requiredIndexes);

        var hasGuard = diff.Exists && appendOnlyGuards.TryGetValue(tableName, out var v) && v;

        return new OperationalRegisterPhysicalTableHealth(
            TableName: tableName,
            Exists: diff.Exists,
            MissingColumns: diff.MissingColumns,
            MissingIndexes: diff.MissingIndexes,
            HasAppendOnlyGuard: hasGuard);
    }

    private static OperationalRegisterPhysicalTableHealth BuildDerivedTableHealth(
        DbSchemaSnapshot snapshot,
        string tableName,
        string[] expectedResourceCols)
    {
        var requiredCols = new List<string> { "period_month", "dimension_set_id" };
        requiredCols.AddRange(expectedResourceCols);

        var requiredIndexes = new[]
        {
            (Columns: ["period_month", "dimension_set_id"], UniqueRequired: true, Label: "unique(period_month, dimension_set_id)"),
            (Columns: ["period_month"], UniqueRequired: false, Label: "index(period_month)"),
            (Columns: new[] { "dimension_set_id" }, UniqueRequired: false, Label: "index(dimension_set_id)"),
        };

        var diff = PostgresPhysicalSchemaHealthHelpers.ComputeTableDiff(snapshot, tableName, requiredCols, requiredIndexes);

        // Derived tables are not append-only; they use replace semantics.
        return new OperationalRegisterPhysicalTableHealth(
            TableName: tableName,
            Exists: diff.Exists,
            MissingColumns: diff.MissingColumns,
            MissingIndexes: diff.MissingIndexes,
            HasAppendOnlyGuard: null);
    }

    private sealed class ResourceRow
    {
        public Guid RegisterId { get; init; }
        public string ColumnCode { get; init; } = null!;
    }
}
