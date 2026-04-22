using NGB.Contracts.Reporting;

namespace NGB.Runtime.Reporting.Planning;

public sealed record ReportPlanSort(
    string FieldCode,
    string? MeasureCode,
    ReportSortDirection Direction,
    ReportTimeGrain? TimeGrain = null,
    bool AppliesToColumnAxis = false,
    string? GroupKey = null);
