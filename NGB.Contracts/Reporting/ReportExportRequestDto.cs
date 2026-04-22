namespace NGB.Contracts.Reporting;

public sealed record ReportExportRequestDto(
    ReportLayoutDto? Layout = null,
    IReadOnlyDictionary<string, ReportFilterValueDto>? Filters = null,
    IReadOnlyDictionary<string, string>? Parameters = null,
    string? VariantCode = null);
