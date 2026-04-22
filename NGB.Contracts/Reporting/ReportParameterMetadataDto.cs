namespace NGB.Contracts.Reporting;

public sealed record ReportParameterMetadataDto(
    string Code,
    string DataType,
    bool IsRequired,
    string? Description = null,
    string? DefaultValue = null,
    string? Label = null);
