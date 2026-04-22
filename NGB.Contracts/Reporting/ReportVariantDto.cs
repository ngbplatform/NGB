namespace NGB.Contracts.Reporting;

public sealed record ReportVariantDto(
    string VariantCode,
    string ReportCode,
    string Name,
    ReportLayoutDto? Layout = null,
    IReadOnlyDictionary<string, ReportFilterValueDto>? Filters = null,
    IReadOnlyDictionary<string, string>? Parameters = null,
    bool IsDefault = false,
    bool IsShared = false);
