namespace NGB.Contracts.Reporting;

public sealed record ReportLayoutDto(
    IReadOnlyList<ReportGroupingDto>? RowGroups = null,
    IReadOnlyList<ReportGroupingDto>? ColumnGroups = null,
    IReadOnlyList<ReportMeasureSelectionDto>? Measures = null,
    IReadOnlyList<string>? DetailFields = null,
    IReadOnlyList<ReportSortDto>? Sorts = null,
    bool ShowDetails = false,
    bool ShowSubtotals = true,
    bool ShowSubtotalsOnSeparateRows = true,
    bool ShowGrandTotals = true);
