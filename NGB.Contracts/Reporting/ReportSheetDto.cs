namespace NGB.Contracts.Reporting;

public sealed record ReportSheetDto(
    IReadOnlyList<ReportSheetColumnDto> Columns,
    IReadOnlyList<ReportSheetRowDto> Rows,
    ReportSheetMetaDto? Meta = null,
    IReadOnlyList<ReportSheetRowDto>? HeaderRows = null);
