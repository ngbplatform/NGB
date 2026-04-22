namespace NGB.Contracts.Reporting;

public sealed record ReportSheetMetaDto(
    string? Title = null,
    string? Subtitle = null,
    bool IsPivot = false,
    bool HasRowOutline = false,
    bool HasColumnGroups = false,
    IReadOnlyDictionary<string, string>? Diagnostics = null);
