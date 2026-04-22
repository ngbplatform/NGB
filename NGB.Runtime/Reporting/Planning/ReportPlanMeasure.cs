using NGB.Contracts.Reporting;

namespace NGB.Runtime.Reporting.Planning;

public sealed record ReportPlanMeasure(
    string MeasureCode,
    string OutputCode,
    string Label,
    string DataType,
    ReportAggregationKind Aggregation,
    string? FormatOverride = null);
