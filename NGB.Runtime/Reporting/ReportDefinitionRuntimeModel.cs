using NGB.Contracts.Reporting;
using NGB.Tools.Exceptions;
using NGB.Tools.Normalization;

namespace NGB.Runtime.Reporting;

public sealed class ReportDefinitionRuntimeModel
{
    public ReportDefinitionRuntimeModel(ReportDefinitionDto definition)
    {
        if (definition is null)
            throw new NgbConfigurationViolationException("Reporting definition is not configured.");

        Definition = definition;
        ReportCodeNorm = CodeNormalizer.NormalizeCodeNorm(definition.ReportCode, nameof(definition.ReportCode));
        Capabilities = definition.Capabilities ?? new ReportCapabilitiesDto();
        DefaultLayout = definition.DefaultLayout ?? new ReportLayoutDto();
        Dataset = definition.Dataset is null ? null : new ReportDatasetDefinition(definition.Dataset);
    }

    public ReportDefinitionDto Definition { get; }
    public string ReportCodeNorm { get; }
    public ReportCapabilitiesDto Capabilities { get; }
    public ReportLayoutDto DefaultLayout { get; }
    public ReportDatasetDefinition? Dataset { get; }

    public ReportLayoutDto GetEffectiveLayout(ReportExecutionRequestDto request)
    {
        if (request is null)
            throw new NgbArgumentRequiredException(nameof(request));

        return request.Layout ?? DefaultLayout;
    }
}
