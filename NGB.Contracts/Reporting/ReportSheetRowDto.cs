namespace NGB.Contracts.Reporting;

public sealed record ReportSheetRowDto(
    ReportRowKind RowKind,
    IReadOnlyList<ReportCellDto> Cells,
    int OutlineLevel = 0,
    bool IsExpanded = true,
    string? GroupKey = null,
    string? SemanticRole = null);
