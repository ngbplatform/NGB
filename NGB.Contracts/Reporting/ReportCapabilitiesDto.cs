namespace NGB.Contracts.Reporting;

public sealed record ReportCapabilitiesDto(
    bool AllowsFilters = true,
    bool AllowsRowGroups = false,
    bool AllowsColumnGroups = false,
    bool AllowsMeasures = true,
    bool AllowsDetailFields = false,
    bool AllowsSorting = false,
    bool AllowsShowDetails = false,
    bool AllowsSubtotals = false,
    bool AllowsSeparateRowSubtotals = false,
    bool AllowsGrandTotals = true,
    bool AllowsVariants = false,
    bool AllowsXlsxExport = false,
    int? MaxRowGroupDepth = null,
    int? MaxColumnGroupDepth = null,
    int? MaxVisibleColumns = null,
    int? MaxVisibleRows = null,
    int? MaxRenderedCells = null);
