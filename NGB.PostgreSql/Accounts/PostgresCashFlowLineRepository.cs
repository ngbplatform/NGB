using Dapper;
using NGB.Accounting.CashFlow;
using NGB.Persistence.Accounts;
using NGB.Persistence.UnitOfWork;

namespace NGB.PostgreSql.Accounts;

public sealed class PostgresCashFlowLineRepository(IUnitOfWork uow) : ICashFlowLineRepository
{
    public async Task<IReadOnlyList<CashFlowLineDefinition>> GetAllAsync(CancellationToken ct = default)
    {
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
                           SELECT
                               line_code  AS "LineCode",
                               method     AS "Method",
                               section    AS "Section",
                               label      AS "Label",
                               sort_order AS "SortOrder",
                               is_system  AS "IsSystem"
                           FROM accounting_cash_flow_lines
                           ORDER BY method, section, sort_order, line_code;
                           """;

        var cmd = new CommandDefinition(sql, transaction: uow.Transaction, cancellationToken: ct);
        return (await uow.Connection.QueryAsync<Row>(cmd))
            .Select(ToDefinition)
            .ToArray();
    }

    public async Task<CashFlowLineDefinition?> GetByCodeAsync(string lineCode, CancellationToken ct = default)
    {
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
                           SELECT
                               line_code  AS "LineCode",
                               method     AS "Method",
                               section    AS "Section",
                               label      AS "Label",
                               sort_order AS "SortOrder",
                               is_system  AS "IsSystem"
                           FROM accounting_cash_flow_lines
                           WHERE line_code = @LineCode
                           LIMIT 1;
                           """;

        var cmd = new CommandDefinition(sql, new { LineCode = lineCode }, transaction: uow.Transaction, cancellationToken: ct);
        var row = await uow.Connection.QuerySingleOrDefaultAsync<Row>(cmd);
        return row is null ? null : ToDefinition(row);
    }

    private static CashFlowLineDefinition ToDefinition(Row row)
        => new(
            row.LineCode,
            (CashFlowMethod)row.Method,
            (CashFlowSection)row.Section,
            row.Label,
            row.SortOrder,
            row.IsSystem);

    private sealed class Row
    {
        public string LineCode { get; init; } = string.Empty;
        public short Method { get; init; }
        public short Section { get; init; }
        public string Label { get; init; } = string.Empty;
        public int SortOrder { get; init; }
        public bool IsSystem { get; init; }
    }
}
