using NGB.Contracts.Reporting;
using NGB.Tools.Exceptions;
using NGB.Tools.Normalization;

namespace NGB.Runtime.Reporting;

public sealed record ReportDatasetMeasureDefinition
{
    public required string CodeNorm { get; init; }
    public required ReportMeasureDto Measure { get; init; }
    public IReadOnlySet<ReportAggregationKind> SupportedAggregations { get; init; } = new HashSet<ReportAggregationKind>();

    public static ReportDatasetMeasureDefinition FromDto(ReportMeasureDto measure)
    {
        if (measure is null)
            throw new NgbConfigurationViolationException("Reporting dataset measure definition is not configured.");

        return new ReportDatasetMeasureDefinition
        {
            CodeNorm = CodeNormalizer.NormalizeCodeNorm(measure.Code, nameof(measure.Code)),
            Measure = measure,
            SupportedAggregations = new HashSet<ReportAggregationKind>(
                measure.SupportedAggregations ?? [],
                EqualityComparer<ReportAggregationKind>.Default)
        };
    }

    public bool SupportsAggregation(ReportAggregationKind aggregation)
        => SupportedAggregations.Count == 0 || SupportedAggregations.Contains(aggregation);

    public ReportAggregationKind ResolveAggregation(ReportAggregationKind requestedAggregation)
    {
        if (SupportsAggregation(requestedAggregation))
            return requestedAggregation;

        if (requestedAggregation == ReportAggregationKind.Sum && SupportedAggregations.Count == 1)
            return SupportedAggregations.Single();

        return requestedAggregation;
    }
}
