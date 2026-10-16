using NGB.Contracts.Reporting;

namespace NGB.Application.Abstractions.Services;

/// <summary>Authorized interactive reports. Callers share the host's report admission budget.</summary>
public interface IAuthorizedReportReadService
{
    Task<IReadOnlyList<ReportDefinitionDto>> GetAllDefinitionsAsync(CancellationToken ct);
    Task<ReportDefinitionDto> GetDefinitionAsync(string reportCode, CancellationToken ct);

    Task<ReportExecutionResponseDto> ExecuteAsync(
        string reportCode,
        ReportExecutionRequestDto request,
        CancellationToken ct);
}
