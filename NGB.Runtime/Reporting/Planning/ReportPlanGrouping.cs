using NGB.Contracts.Reporting;

namespace NGB.Runtime.Reporting.Planning;

public sealed record ReportPlanGrouping(
    string FieldCode,
    string OutputCode,
    string Label,
    string DataType,
    bool IsColumnAxis,
    ReportTimeGrain? TimeGrain = null,
    bool IncludeDetails = false,
    bool IncludeEmpty = false,
    bool IncludeDescendants = false,
    string? GroupKey = null);
