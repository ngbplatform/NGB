using Dapper;
using NGB.Persistence.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.OperationalRegisters.Internal;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PostgreSql.OperationalRegisters;

/// <summary>
/// Read-only admin read model for Operational Registers.
/// </summary>
public sealed class PostgresOperationalRegisterAdminReader(IUnitOfWork uow) : IOperationalRegisterAdminReader
{
    public async Task<IReadOnlyList<OperationalRegisterAdminListItem>> GetListAsync(CancellationToken ct = default)
    {
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
                           SELECT
                               r.register_id     AS "RegisterId",
                               r.code            AS "Code",
                               r.code_norm       AS "CodeNorm",
                               r.table_code      AS "TableCode",
                               r.name            AS "Name",
                               r.has_movements   AS "HasMovements",
                               r.created_at_utc  AS "CreatedAtUtc",
                               r.updated_at_utc  AS "UpdatedAtUtc",

                               COALESCE(res.cnt, 0) AS "ResourcesCount",
                               COALESCE(dim.cnt, 0) AS "DimensionRulesCount"
                           FROM operational_registers r
                           LEFT JOIN (
                               SELECT register_id, COUNT(*)::int4 AS cnt
                               FROM operational_register_resources
                               GROUP BY register_id
                           ) res ON res.register_id = r.register_id
                           LEFT JOIN (
                               SELECT register_id, COUNT(*)::int4 AS cnt
                               FROM operational_register_dimension_rules
                               GROUP BY register_id
                           ) dim ON dim.register_id = r.register_id
                           ORDER BY r.code_norm;
                           """;

        var cmd = new CommandDefinition(sql, transaction: uow.Transaction, cancellationToken: ct);
        var rows = (await uow.Connection.QueryAsync<ListRow>(cmd)).AsList();

        if (rows.Count == 0)
            return [];

        return rows.Select(x => x.ToItem()).ToArray();
    }

    public async Task<OperationalRegisterAdminDetails?> GetDetailsByIdAsync(
        Guid registerId,
        CancellationToken ct = default)
    {
        registerId.EnsureNonEmpty(nameof(registerId));
        await uow.EnsureConnectionOpenAsync(ct);

        var reg = await GetRegisterRowByIdAsync(registerId, ct);
        if (reg is null)
            return null;

        var resources = await GetResourcesAsync(registerId, ct);
        var rules = await GetDimensionRulesAsync(registerId, ct);

        return new OperationalRegisterAdminDetails(reg.ToItem(), resources, rules);
    }

    public async Task<OperationalRegisterAdminDetails?> GetDetailsByCodeAsync(
        string code,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new NgbArgumentRequiredException(nameof(code));

        var codeNorm = NormalizeCodeNorm(code);

        await uow.EnsureConnectionOpenAsync(ct);

        var reg = await GetRegisterRowByCodeNormAsync(codeNorm, ct);
        if (reg is null)
            return null;

        var resources = await GetResourcesAsync(reg.RegisterId, ct);
        var rules = await GetDimensionRulesAsync(reg.RegisterId, ct);

        return new OperationalRegisterAdminDetails(reg.ToItem(), resources, rules);
    }

    private async Task<OperationalRegisterAdminItemRow?> GetRegisterRowByIdAsync(Guid registerId, CancellationToken ct)
    {
        const string sql = """
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
                           WHERE register_id = @RegisterId
                           LIMIT 1;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new { RegisterId = registerId },
            transaction: uow.Transaction,
            cancellationToken: ct);

        return await uow.Connection.QuerySingleOrDefaultAsync<OperationalRegisterAdminItemRow>(cmd);
    }

    private async Task<OperationalRegisterAdminItemRow?> GetRegisterRowByCodeNormAsync(
        string codeNorm,
        CancellationToken ct)
    {
        const string sql = """
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
                           WHERE code_norm = @CodeNorm
                           LIMIT 1;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new { CodeNorm = codeNorm },
            transaction: uow.Transaction,
            cancellationToken: ct);

        return await uow.Connection.QuerySingleOrDefaultAsync<OperationalRegisterAdminItemRow>(cmd);
    }

    private async Task<IReadOnlyList<OperationalRegisterResource>> GetResourcesAsync(
        Guid registerId,
        CancellationToken ct)
    {
        const string sql = """
                           SELECT
                               code        AS "Code",
                               code_norm   AS "CodeNorm",
                               column_code AS "ColumnCode",
                               name        AS "Name",
                               ordinal     AS "Ordinal"
                           FROM operational_register_resources
                           WHERE register_id = @RegisterId
                           ORDER BY ordinal, code_norm;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new { RegisterId = registerId },
            transaction: uow.Transaction,
            cancellationToken: ct);

        var rows = (await uow.Connection.QueryAsync<ResourceRow>(cmd)).AsList();
        if (rows.Count == 0)
            return [];

        return rows.Select(x => x.ToResource()).ToArray();
    }

    private async Task<IReadOnlyList<OperationalRegisterDimensionRule>> GetDimensionRulesAsync(
        Guid registerId,
        CancellationToken ct)
    {
        const string sql = """
                           SELECT
                               r.dimension_id AS "DimensionId",
                               d.code         AS "DimensionCode",
                               r.ordinal      AS "Ordinal",
                               r.is_required  AS "IsRequired"
                           FROM operational_register_dimension_rules r
                           JOIN platform_dimensions d ON d.dimension_id = r.dimension_id
                           WHERE r.register_id = @RegisterId
                           ORDER BY r.ordinal, d.code;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new { RegisterId = registerId },
            transaction: uow.Transaction,
            cancellationToken: ct);

        var rows = (await uow.Connection.QueryAsync<RuleRow>(cmd)).AsList();
        if (rows.Count == 0)
            return [];

        return rows.Select(x => x.ToRule()).ToArray();
    }

    private static string NormalizeCodeNorm(string code) => code.Trim().ToLowerInvariant();

    private sealed class ListRow : OperationalRegisterAdminItemRow
    {
        public int ResourcesCount { get; init; }
        public int DimensionRulesCount { get; init; }

        public new OperationalRegisterAdminListItem ToItem() => new(base.ToItem(), ResourcesCount, DimensionRulesCount);
    }

    private sealed class ResourceRow
    {
        public string Code { get; init; } = null!;
        public string CodeNorm { get; init; } = null!;
        public string ColumnCode { get; init; } = null!;
        public string Name { get; init; } = null!;
        public int Ordinal { get; init; }

        public OperationalRegisterResource ToResource() => new(Code, CodeNorm, ColumnCode, Name, Ordinal);
    }

    private sealed class RuleRow
    {
        public Guid DimensionId { get; init; }
        public string DimensionCode { get; init; } = null!;
        public int Ordinal { get; init; }
        public bool IsRequired { get; init; }

        public OperationalRegisterDimensionRule ToRule() => new(DimensionId, DimensionCode, Ordinal, IsRequired);
    }
}
