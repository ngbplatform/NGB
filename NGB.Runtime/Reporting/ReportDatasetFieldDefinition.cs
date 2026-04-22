using NGB.Contracts.Reporting;
using NGB.Tools.Exceptions;
using NGB.Tools.Normalization;

namespace NGB.Runtime.Reporting;

public sealed record ReportDatasetFieldDefinition
{
    public required string CodeNorm { get; init; }
    public required ReportFieldDto Field { get; init; }
    public IReadOnlySet<ReportTimeGrain> SupportedTimeGrains { get; init; } = new HashSet<ReportTimeGrain>();

    public static ReportDatasetFieldDefinition FromDto(ReportFieldDto field)
    {
        if (field is null)
            throw new NgbConfigurationViolationException("Reporting dataset field definition is not configured.");

        var codeNorm = CodeNormalizer.NormalizeCodeNorm(field.Code, nameof(field.Code));
        var supportedTimeGrains = new HashSet<ReportTimeGrain>(
            field.SupportedTimeGrains ?? [],
            EqualityComparer<ReportTimeGrain>.Default);

        if (supportedTimeGrains.Count > 0 && field.Kind != ReportFieldKind.Time)
        {
            throw new NgbConfigurationViolationException(
                $"Dataset field '{codeNorm}' declares time grains but is not a time field.",
                new Dictionary<string, object?>
                {
                    ["fieldCode"] = codeNorm,
                    ["fieldKind"] = field.Kind.ToString()
                });
        }

        return new ReportDatasetFieldDefinition
        {
            CodeNorm = codeNorm,
            Field = field,
            SupportedTimeGrains = supportedTimeGrains
        };
    }

    public bool SupportsTimeGrain(ReportTimeGrain? timeGrain)
        => timeGrain is null || SupportedTimeGrains.Contains(timeGrain.Value);
}
