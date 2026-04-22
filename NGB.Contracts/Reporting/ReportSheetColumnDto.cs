namespace NGB.Contracts.Reporting;

public sealed record ReportSheetColumnDto(
    string Code,
    string Title,
    string DataType,
    int Width = 0,
    bool IsFrozen = false,
    string? SemanticRole = null);
