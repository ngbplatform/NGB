using EquipmentInspection.Definitions;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Metadata;
using NGB.Contracts.Reporting;

namespace EquipmentInspection.Runtime;

public sealed class InspectionReportDefinitions : IReportDefinitionSource, IReportDatasetSource
{
    public IReadOnlyList<ReportDefinitionDto> GetDefinitions() =>
    [
        new ReportDefinitionDto(
            ReportCode: InspectionReports.Summary,
            Name: "Inspection summary",
            Group: "Equipment inspection",
            Description: "Posted inspections by result within the inclusive UTC date range.",
            Mode: ReportExecutionMode.Composable,
            Dataset: Dataset(),
            Capabilities: new ReportCapabilitiesDto(
                AllowsFilters: true,
                AllowsRowGroups: true,
                AllowsMeasures: true,
                AllowsSorting: true,
                AllowsGrandTotals: true,
                AllowsXlsxExport: true,
                AllowsVariants: true,
                MaxRowGroupDepth: 1,
                MaxVisibleColumns: 4,
                MaxVisibleRows: 1_000,
                MaxRenderedCells: 4_000),
            DefaultLayout: new ReportLayoutDto(
                RowGroups: [new ReportGroupingDto("result")],
                Measures: [new ReportMeasureSelectionDto("inspection_count", ReportAggregationKind.Count)],
                Sorts: [new ReportSortDto("result")],
                ShowSubtotals: false,
                ShowGrandTotals: true),
            Parameters:
            [
                new ReportParameterMetadataDto("from_utc", "date", true, Label: "From"),
                new ReportParameterMetadataDto("to_utc", "date", true, Label: "To")
            ],
            Filters:
            [
                new ReportFilterFieldDto("equipment_id", "Equipment", "uuid", IsMulti: true,
                    Lookup: new CatalogLookupSourceDto(EquipmentDefinitions.EquipmentCode)),
                new ReportFilterFieldDto("result", "Result", "string")
            ],
            Presentation: new ReportPresentationDto(InitialPageSize: 50, RowNoun: "results",
                EmptyStateMessage: "No posted inspections match the selected filters."))
    ];

    public IReadOnlyList<ReportDatasetDto> GetDatasets() => [Dataset()];

    private static ReportDatasetDto Dataset() => new(
        DatasetCode: InspectionReports.Summary,
        Fields:
        [
            new ReportFieldDto("equipment_id", "Equipment", "uuid", ReportFieldKind.Dimension,
                IsFilterable: true, Lookup: new CatalogLookupSourceDto(EquipmentDefinitions.EquipmentCode)),
            new ReportFieldDto("result", "Result", "string", ReportFieldKind.Dimension,
                IsFilterable: true, IsGroupable: true, IsSortable: true)
        ],
        Measures:
        [
            new ReportMeasureDto("inspection_count", "Inspections", "int64", [ReportAggregationKind.Count])
        ]);
}
