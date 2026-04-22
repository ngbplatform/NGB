using NGB.Contracts.Reporting;

namespace NGB.Application.Abstractions.Services;

public interface IReportDefinitionEnricher
{
    ReportDefinitionDto Enrich(ReportDefinitionDto definition);
}
