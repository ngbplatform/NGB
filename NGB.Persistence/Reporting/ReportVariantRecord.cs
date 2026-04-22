namespace NGB.Persistence.Reporting;

public sealed record ReportVariantRecord(
    Guid ReportVariantId,
    string ReportCode,
    string ReportCodeNorm,
    string VariantCode,
    string VariantCodeNorm,
    Guid? OwnerPlatformUserId,
    string Name,
    string? LayoutJson,
    string? FiltersJson,
    string? ParametersJson,
    bool IsDefault,
    bool IsShared,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
