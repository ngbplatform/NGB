using Dapper;

namespace NGB.PostgreSql.Reporting;

public sealed record PostgresReportSqlStatement(
    string Sql,
    DynamicParameters Parameters,
    IReadOnlyList<PostgresReportOutputColumn> Columns,
    bool IsAggregated,
    int Offset,
    int Limit);
