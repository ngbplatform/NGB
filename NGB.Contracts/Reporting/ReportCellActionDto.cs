namespace NGB.Contracts.Reporting;

public sealed record ReportCellActionDto(
    string Kind,
    string? DocumentType = null,
    Guid? DocumentId = null,
    Guid? AccountId = null,
    string? CatalogType = null,
    Guid? CatalogId = null,
    ReportCellReportTargetDto? Report = null);
