using NGB.Contracts.Reporting;

namespace NGB.Application.Abstractions.Services;

public interface IReportDefinitionSource
{
    IReadOnlyList<ReportDefinitionDto> GetDefinitions();
}
