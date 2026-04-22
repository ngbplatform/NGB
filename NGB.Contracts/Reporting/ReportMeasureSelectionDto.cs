namespace NGB.Contracts.Reporting;

public sealed record ReportMeasureSelectionDto(
    string MeasureCode,
    ReportAggregationKind Aggregation = ReportAggregationKind.Sum,
    string? LabelOverride = null,
    string? FormatOverride = null);
