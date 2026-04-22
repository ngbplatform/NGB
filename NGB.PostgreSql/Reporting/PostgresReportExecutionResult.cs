namespace NGB.PostgreSql.Reporting;

public sealed record PostgresReportExecutionResult(
    IReadOnlyList<PostgresReportOutputColumn> Columns,
    IReadOnlyList<PostgresReportExecutionRow> Rows,
    int Offset,
    int Limit,
    bool HasMore,
    string? NextCursor = null,
    int? Total = null,
    IReadOnlyDictionary<string, string>? Diagnostics = null);

public sealed record PostgresReportExecutionRow(IReadOnlyDictionary<string, object?> Values);

public sealed record PostgresReportOutputColumn(
    string OutputCode,
    string Title,
    string DataType,
    string? SemanticRole = null);
