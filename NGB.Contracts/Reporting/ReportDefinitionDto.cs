namespace NGB.Contracts.Reporting;

public sealed record ReportDefinitionDto(
    string ReportCode,
    string Name,
    string? Group = null,
    string? Description = null,
    ReportExecutionMode Mode = ReportExecutionMode.Canonical,
    ReportDatasetDto? Dataset = null,
    ReportCapabilitiesDto? Capabilities = null,
    ReportLayoutDto? DefaultLayout = null,
    IReadOnlyList<ReportParameterMetadataDto>? Parameters = null,
    IReadOnlyList<ReportFilterFieldDto>? Filters = null,
    ReportPresentationDto? Presentation = null);
