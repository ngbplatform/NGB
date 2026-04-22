namespace NGB.Contracts.Reporting;

public sealed record ReportSortDto(
    string FieldCode,
    ReportSortDirection Direction = ReportSortDirection.Asc,
    ReportTimeGrain? TimeGrain = null,
    bool AppliesToColumnAxis = false,
    string? GroupKey = null);
