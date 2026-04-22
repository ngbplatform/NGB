using NGB.Application.Abstractions.Services;
using NGB.Contracts.Metadata;
using NGB.Contracts.Reporting;
using NGB.PropertyManagement.Reporting;
using NGB.Runtime.Reporting.Definitions;

namespace NGB.PropertyManagement.Runtime.Reporting;

public sealed class PropertyManagementCanonicalReportDefinitionSource : IReportDefinitionSource
{
    public IReadOnlyList<ReportDefinitionDto> GetDefinitions()
        =>
        [
            new(
                ReportCode: "pm.building.summary",
                Name: "Building Summary",
                Group: "Portfolio",
                Description: "Canonical PM building summary",
                Mode: ReportExecutionMode.Canonical,
                Capabilities: new ReportCapabilitiesDto(
                    AllowsFilters: true,
                    AllowsRowGroups: false,
                    AllowsColumnGroups: false,
                    AllowsMeasures: false,
                    AllowsDetailFields: false,
                    AllowsSorting: false,
                    AllowsShowDetails: false,
                    AllowsSubtotals: false,
                    AllowsGrandTotals: true,
                    AllowsVariants: true,
                    AllowsXlsxExport: true,
                    MaxVisibleColumns: 6,
                    MaxVisibleRows: 2,
                    MaxRenderedCells: 24),
                DefaultLayout: new ReportLayoutDto(
                    ShowDetails: false,
                    ShowSubtotals: false,
                    ShowGrandTotals: true),
                Parameters:
                [
                    new ReportParameterMetadataDto(
                        "as_of_utc",
                        "date",
                        false,
                        Label: "As of")
                ],
                Filters:
                [
                    new ReportFilterFieldDto(
                        "building_id",
                        "Building",
                        "uuid",
                        IsRequired: true,
                        Lookup: new CatalogLookupSourceDto("pm.property"))
                ]),
            new(
                ReportCode: "pm.occupancy.summary",
                Name: "Occupancy Summary",
                Group: "Portfolio",
                Description: "Canonical PM occupancy summary by building",
                Mode: ReportExecutionMode.Canonical,
                Capabilities: new ReportCapabilitiesDto(
                    AllowsFilters: true,
                    AllowsRowGroups: false,
                    AllowsColumnGroups: false,
                    AllowsMeasures: false,
                    AllowsDetailFields: false,
                    AllowsSorting: false,
                    AllowsShowDetails: false,
                    AllowsSubtotals: false,
                    AllowsGrandTotals: true,
                    AllowsVariants: true,
                    AllowsXlsxExport: true,
                    MaxVisibleColumns: 6,
                    MaxVisibleRows: 500,
                    MaxRenderedCells: 3_500),
                DefaultLayout: new ReportLayoutDto(
                    ShowDetails: false,
                    ShowSubtotals: false,
                    ShowGrandTotals: true),
                Parameters:
                [
                    new ReportParameterMetadataDto(
                        "as_of_utc",
                        "date",
                        false,
                        Label: "As of")
                ],
                Filters:
                [
                    new ReportFilterFieldDto(
                        "building_id",
                        "Building",
                        "uuid",
                        IsRequired: false,
                        Lookup: new CatalogLookupSourceDto("pm.property"))
                ]),
            new(
                ReportCode: "pm.maintenance.queue",
                Name: "Open Queue",
                Group: "Maintenance",
                Description: "Canonical log of all open maintenance tasks",
                Mode: ReportExecutionMode.Canonical,
                Capabilities: new ReportCapabilitiesDto(
                    AllowsFilters: true,
                    AllowsRowGroups: false,
                    AllowsColumnGroups: false,
                    AllowsMeasures: false,
                    AllowsDetailFields: false,
                    AllowsSorting: false,
                    AllowsShowDetails: false,
                    AllowsSubtotals: false,
                    AllowsGrandTotals: false,
                    AllowsVariants: true,
                    AllowsXlsxExport: true,
                    MaxVisibleColumns: 13,
                    MaxVisibleRows: 500,
                    MaxRenderedCells: 6_500),
                DefaultLayout: new ReportLayoutDto(
                    ShowDetails: false,
                    ShowSubtotals: false,
                    ShowGrandTotals: false),
                Parameters:
                [
                    new ReportParameterMetadataDto(
                        "as_of_utc",
                        "date",
                        false,
                        Label: "As of")
                ],
                Filters: CreateMaintenanceQueueFilters()),
            new(
                ReportCode: PropertyManagementCodes.TenantStatement,
                Name: "Tenant Statement",
                Group: "Receivables",
                Description: "Canonical tenant statement for a lease",
                Mode: ReportExecutionMode.Canonical,
                Capabilities: new ReportCapabilitiesDto(
                    AllowsFilters: true,
                    AllowsRowGroups: false,
                    AllowsColumnGroups: false,
                    AllowsMeasures: false,
                    AllowsDetailFields: false,
                    AllowsSorting: false,
                    AllowsShowDetails: false,
                    AllowsSubtotals: false,
                    AllowsGrandTotals: true,
                    AllowsVariants: true,
                    AllowsXlsxExport: true,
                    MaxVisibleColumns: 7,
                    MaxVisibleRows: 500,
                    MaxRenderedCells: 4_000),
                DefaultLayout: new ReportLayoutDto(
                    ShowDetails: false,
                    ShowSubtotals: false,
                    ShowGrandTotals: true),
                Parameters:
                [
                    new ReportParameterMetadataDto(
                        "from_utc",
                        "date",
                        false,
                        Label: "From"),
                    new ReportParameterMetadataDto(
                        "to_utc",
                        "date",
                        false,
                        Label: "To")
                ],
                Filters: CreateReceivablesLeaseFilter()),
            new(
                ReportCode: "pm.receivables.aging",
                Name: "Aging",
                Group: "Receivables",
                Description: "Canonical receivables aging buckets",
                Mode: ReportExecutionMode.Canonical,
                Capabilities: new ReportCapabilitiesDto(
                    AllowsFilters: true,
                    AllowsRowGroups: false,
                    AllowsColumnGroups: false,
                    AllowsMeasures: false,
                    AllowsDetailFields: false,
                    AllowsSorting: false,
                    AllowsShowDetails: false,
                    AllowsSubtotals: false,
                    AllowsGrandTotals: true,
                    AllowsVariants: true,
                    AllowsXlsxExport: true,
                    MaxVisibleColumns: 8,
                    MaxVisibleRows: 500,
                    MaxRenderedCells: 4_000),
                DefaultLayout: new ReportLayoutDto(
                    ShowDetails: false,
                    ShowSubtotals: false,
                    ShowGrandTotals: true),
                Parameters:
                [
                    new ReportParameterMetadataDto(
                        "as_of_utc",
                        "date",
                        true,
                        Label: "As of")
                ],
                Filters: CreateReceivablesLeaseFilter()),
            new(
                ReportCode: "pm.receivables.open_items",
                Name: "Open Items Report",
                Group: "Receivables",
                Description: "Canonical receivables open-items list",
                Mode: ReportExecutionMode.Canonical,
                Capabilities: new ReportCapabilitiesDto(
                    AllowsFilters: true,
                    AllowsRowGroups: false,
                    AllowsColumnGroups: false,
                    AllowsMeasures: false,
                    AllowsDetailFields: false,
                    AllowsSorting: false,
                    AllowsShowDetails: false,
                    AllowsSubtotals: false,
                    AllowsGrandTotals: true,
                    AllowsVariants: true,
                    AllowsXlsxExport: true,
                    MaxVisibleColumns: 4,
                    MaxVisibleRows: 500,
                    MaxRenderedCells: 2_000),
                DefaultLayout: new ReportLayoutDto(
                    ShowDetails: false,
                    ShowSubtotals: false,
                    ShowGrandTotals: true),
                Filters: CreateReceivablesLeaseFilter()),
            new(
                ReportCode: "pm.receivables.open_items.details",
                Name: "Open Items Detail",
                Group: "Receivables",
                Description: "Canonical receivables open-items details",
                Mode: ReportExecutionMode.Canonical,
                Capabilities: new ReportCapabilitiesDto(
                    AllowsFilters: true,
                    AllowsRowGroups: false,
                    AllowsColumnGroups: false,
                    AllowsMeasures: false,
                    AllowsDetailFields: false,
                    AllowsSorting: false,
                    AllowsShowDetails: false,
                    AllowsSubtotals: false,
                    AllowsGrandTotals: true,
                    AllowsVariants: true,
                    AllowsXlsxExport: true,
                    MaxVisibleColumns: 8,
                    MaxVisibleRows: 500,
                    MaxRenderedCells: 4_000),
                DefaultLayout: new ReportLayoutDto(
                    ShowDetails: false,
                    ShowSubtotals: false,
                    ShowGrandTotals: true),
                Filters: CreateReceivablesLeaseFilter())
        ];

    private static ReportFilterFieldDto[] CreateMaintenanceQueueFilters()
        =>
        [
            new(
                "building_id",
                "Building",
                "uuid",
                IsRequired: false,
                Lookup: new CatalogLookupSourceDto("pm.property")),
            new(
                "property_id",
                "Property",
                "uuid",
                IsRequired: false,
                Lookup: new CatalogLookupSourceDto("pm.property")),
            new(
                "category_id",
                "Category",
                "uuid",
                IsRequired: false,
                Lookup: new CatalogLookupSourceDto("pm.maintenance_category")),
            new(
                "priority",
                "Priority",
                "string",
                IsRequired: false,
                Options: CreateMaintenancePriorityOptions()),
            new(
                "assigned_party_id",
                "Assigned To",
                "uuid",
                IsRequired: false,
                Lookup: new CatalogLookupSourceDto("pm.party")),
            new(
                "queue_state",
                "Queue State",
                "string",
                IsRequired: false,
                Options: ReportFilterOptionTools.ToReportFilterOptions<MaintenanceQueueState>())
        ];

    private static ReportFilterFieldDto[] CreateReceivablesLeaseFilter()
        =>
        [
            new(
                "lease_id",
                "Lease",
                "uuid",
                IsRequired: true,
                Lookup: new DocumentLookupSourceDto(["pm.lease"]))
        ];

    private static ReportFilterOptionDto[] CreateMaintenancePriorityOptions()
        =>
        [
            new("Emergency", "Emergency"),
            new("High", "High"),
            new("Normal", "Normal"),
            new("Low", "Low")
        ];
}
