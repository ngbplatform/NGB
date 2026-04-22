namespace NGB.Contracts.Reporting;

public sealed record ReportMeasureDto(
    string Code,
    string Label,
    string DataType,
    IReadOnlyList<ReportAggregationKind>? SupportedAggregations = null,
    string? Format = null,
    string? Description = null);
