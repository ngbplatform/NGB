namespace NGB.Contracts.Reporting;

public sealed record ReportExecutionResponseDto(
    ReportSheetDto Sheet,
    int Offset,
    int Limit,
    int? Total,
    bool HasMore,
    string? NextCursor,
    IReadOnlyDictionary<string, string>? Diagnostics = null);
