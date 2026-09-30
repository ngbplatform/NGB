using NGB.Contracts.Reporting;

namespace NGB.PostgreSql.Reporting;

public sealed record PostgresReportCursorColumn(
    string Alias,
    string DataType,
    ReportSortDirection Direction,
    bool IsHidden);
