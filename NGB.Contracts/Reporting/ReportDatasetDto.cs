namespace NGB.Contracts.Reporting;

public sealed record ReportDatasetDto(
    string DatasetCode,
    IReadOnlyList<ReportFieldDto>? Fields = null,
    IReadOnlyList<ReportMeasureDto>? Measures = null);
