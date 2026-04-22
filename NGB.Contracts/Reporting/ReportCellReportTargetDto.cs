namespace NGB.Contracts.Reporting;

public sealed record ReportCellReportTargetDto(
    string ReportCode,
    IReadOnlyDictionary<string, string>? Parameters = null,
    IReadOnlyDictionary<string, ReportFilterValueDto>? Filters = null);
