namespace NGB.Contracts.Reporting;

public sealed record ReportGroupingDto(
    string FieldCode,
    ReportTimeGrain? TimeGrain = null,
    bool IncludeDetails = false,
    bool IncludeEmpty = false,
    bool IncludeDescendants = false,
    string? LabelOverride = null,
    string? GroupKey = null);
