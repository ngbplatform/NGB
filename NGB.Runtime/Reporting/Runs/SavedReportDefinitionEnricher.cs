using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;

namespace NGB.Runtime.Reporting.Runs;

public sealed class SavedReportDefinitionEnricher : IReportDefinitionEnricher
{
    public ReportDefinitionDto Enrich(ReportDefinitionDto definition) => definition with
    {
        Capabilities = (definition.Capabilities ?? new()) with
        {
            SupportsSavedExecution = true, MaxVisibleRows = null, MaxRenderedCells = null
        }
    };
}
