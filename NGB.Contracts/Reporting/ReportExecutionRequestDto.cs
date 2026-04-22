namespace NGB.Contracts.Reporting;

public sealed record ReportExecutionRequestDto(
    ReportLayoutDto? Layout = null,
    IReadOnlyDictionary<string, ReportFilterValueDto>? Filters = null,
    IReadOnlyDictionary<string, string>? Parameters = null,
    string? VariantCode = null,
    int Offset = 0,
    int Limit = 200,
    string? Cursor = null,
    bool DisablePaging = false);
