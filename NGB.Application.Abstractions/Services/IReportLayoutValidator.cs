using NGB.Contracts.Reporting;

namespace NGB.Application.Abstractions.Services;

public interface IReportLayoutValidator
{
    void Validate(ReportDefinitionDto definition, ReportExecutionRequestDto request);
}
