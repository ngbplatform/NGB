using Dapper;
using NGB.Persistence.UnitOfWork;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.Internal;

internal static class PostgresRegisterDimensionRulesSql
{
    public const string OperationalRegisterDimensionRulesTable = "operational_register_dimension_rules";
    public const string ReferenceRegisterDimensionRulesTable = "reference_register_dimension_rules";

    public enum PlatformDimensionsUpsertMode
    {
        /// <summary>
        /// INSERT new rows only. Existing rows are left untouched.
        /// </summary>
        DoNothing,

        /// <summary>
        /// INSERT new rows, and for existing DimensionId update Code/Name + UpdatedAtUtc.
        /// </summary>
        UpdateCodeAndName
    }

    public enum DimensionRulesConflictMode
    {
        None,
        DoNothing,
        UpdateOrdinalRequired
    }

    public static string SelectRulesSql(string rulesTable)
    {
        EnsureAllowedRulesTable(rulesTable);

        return $"""
                SELECT
                    r.dimension_id AS "DimensionId",
                    d.code         AS "DimensionCode",
                    r.ordinal      AS "Ordinal",
                    r.is_required  AS "IsRequired"
                FROM {rulesTable} r
                JOIN platform_dimensions d ON d.dimension_id = r.dimension_id
                WHERE r.register_id = @RegisterId
                ORDER BY r.ordinal, d.code;
                """;
    }

    public static async Task UpsertPlatformDimensionsAsync(
        IUnitOfWork uow,
        Guid[] dimensionIds,
        string[] codes,
        string[] names,
        DateTime nowUtc,
        PlatformDimensionsUpsertMode mode,
        CancellationToken ct)
    {
        if (dimensionIds.Length == 0)
            return;

        const string insertDoNothing = """
                                      INSERT INTO platform_dimensions(
                                          dimension_id,
                                          code,
                                          name,
                                          created_at_utc,
                                          updated_at_utc)
                                      SELECT
                                          x.dimension_id,
                                          x.code,
                                          x.name,
                                          @NowUtc,
                                          @NowUtc
                                      FROM UNNEST(@DimensionIds::uuid[], @Codes::text[], @Names::text[])
                                          AS x(dimension_id, code, name)
                                      ON CONFLICT (dimension_id) DO NOTHING;
                                      """;

        const string insertUpdate = """
                                    INSERT INTO platform_dimensions(
                                        dimension_id,
                                        code,
                                        name,
                                        created_at_utc,
                                        updated_at_utc)
                                    SELECT
                                        x.dimension_id,
                                        x.code,
                                        x.name,
                                        @NowUtc,
                                        @NowUtc
                                    FROM UNNEST(@DimensionIds::uuid[], @Codes::text[], @Names::text[])
                                        AS x(dimension_id, code, name)
                                    ON CONFLICT (dimension_id) DO UPDATE
                                        SET code = EXCLUDED.code,
                                            name = EXCLUDED.name,
                                            updated_at_utc = EXCLUDED.updated_at_utc;
                                    """;

        var sql = mode == PlatformDimensionsUpsertMode.DoNothing
            ? insertDoNothing
            : insertUpdate;

        var cmd = new CommandDefinition(
            sql,
            new
            {
                DimensionIds = dimensionIds,
                Codes = codes,
                Names = names,
                NowUtc = nowUtc
            },
            transaction: uow.Transaction,
            cancellationToken: ct);

        await uow.Connection.ExecuteAsync(cmd);
    }

    public static async Task InsertRegisterDimensionRulesAsync(
        IUnitOfWork uow,
        string rulesTable,
        Guid registerId,
        Guid[] dimensionIds,
        int[] ordinals,
        bool[] isRequired,
        DateTime nowUtc,
        DimensionRulesConflictMode conflictMode,
        CancellationToken ct)
    {
        EnsureAllowedRulesTable(rulesTable);

        if (dimensionIds.Length == 0)
            return;

        var conflictClause = conflictMode switch
        {
            DimensionRulesConflictMode.None => string.Empty,
            DimensionRulesConflictMode.DoNothing => "\nON CONFLICT (register_id, dimension_id) DO NOTHING",
            DimensionRulesConflictMode.UpdateOrdinalRequired => "\nON CONFLICT (register_id, dimension_id) DO UPDATE\n    SET ordinal = EXCLUDED.ordinal,\n        is_required = EXCLUDED.is_required,\n        updated_at_utc = EXCLUDED.updated_at_utc",
            _ => throw new NgbArgumentOutOfRangeException(nameof(conflictMode), conflictMode, "Unsupported dimension rules conflict mode.")
        };

        var sql = $"""
                  INSERT INTO {rulesTable}(
                      register_id,
                      dimension_id,
                      ordinal,
                      is_required,
                      created_at_utc,
                      updated_at_utc)
                  SELECT
                      @RegisterId,
                      x.dimension_id,
                      x.ordinal,
                      x.is_required,
                      @NowUtc,
                      @NowUtc
                  FROM UNNEST(@DimensionIds::uuid[], @Ordinals::int4[], @IsRequired::bool[])
                      AS x(dimension_id, ordinal, is_required){conflictClause};
                  """;

        var cmd = new CommandDefinition(
            sql,
            new
            {
                RegisterId = registerId,
                DimensionIds = dimensionIds,
                Ordinals = ordinals,
                IsRequired = isRequired,
                NowUtc = nowUtc
            },
            transaction: uow.Transaction,
            cancellationToken: ct);

        await uow.Connection.ExecuteAsync(cmd);
    }

    private static void EnsureAllowedRulesTable(string table)
    {
        // Safety: only allow the two known tables.
        if (!string.Equals(table, OperationalRegisterDimensionRulesTable, StringComparison.Ordinal)
            && !string.Equals(table, ReferenceRegisterDimensionRulesTable, StringComparison.Ordinal))
        {
            throw new NgbConfigurationViolationException("Unexpected rules table.", new Dictionary<string, object?> { ["table"] = table });
        }

        // Extra hardening: ensure safe identifier shape.
        PostgresSqlIdentifiers.EnsureOrThrow(table, context: "dimension_rules_table");
    }
}
