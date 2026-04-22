using NGB.Contracts.Reporting;

namespace NGB.Application.Abstractions.Services;

public interface IReportDefinitionProvider
{
    Task<IReadOnlyList<ReportDefinitionDto>> GetAllDefinitionsAsync(CancellationToken ct);
    Task<ReportDefinitionDto> GetDefinitionAsync(string reportCode, CancellationToken ct);
}
