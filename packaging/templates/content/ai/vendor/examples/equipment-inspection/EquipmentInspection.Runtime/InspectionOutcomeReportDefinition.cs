using EquipmentInspection.Definitions;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;

namespace EquipmentInspection.Runtime;

public sealed class InspectionOutcomeReportDefinition : IReportDefinitionSource
{
    public IReadOnlyList<ReportDefinitionDto> GetDefinitions() =>
    [
        new ReportDefinitionDto(
            ReportCode: InspectionReports.OutcomeRate,
            Name: "Inspection outcome rate",
            Group: "Equipment inspection",
            Description: "Posted inspections with result Passed as a percentage of all Posted inspections in the UTC date range.",
            Mode: ReportExecutionMode.Canonical,
            Capabilities: new ReportCapabilitiesDto(
                AllowsFilters: false,
                AllowsMeasures: false,
                AllowsGrandTotals: false,
                MaxVisibleColumns: 3,
                MaxVisibleRows: 1,
                MaxRenderedCells: 3),
            DefaultLayout: new ReportLayoutDto(ShowSubtotals: false, ShowGrandTotals: false),
            Parameters:
            [
                new ReportParameterMetadataDto("from_utc", "date", true, Label: "From"),
                new ReportParameterMetadataDto("to_utc", "date", true, Label: "To")
            ],
            Presentation: new ReportPresentationDto(InitialPageSize: 1, RowNoun: "summary"))
    ];
}
