namespace NGB.Contracts.Reporting;

public sealed record ReportPresentationDto(
    int? InitialPageSize = null,
    string? RowNoun = null,
    string? EmptyStateMessage = null,
    ReportGroupedPagingMode GroupedPagingMode = ReportGroupedPagingMode.Standard);
