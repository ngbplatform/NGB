using NGB.Contracts.Reporting;

namespace NGB.Application.Abstractions.Services;

public interface IReportEngine
{
    Task<ReportExecutionResponseDto> ExecuteAsync(
        string reportCode,
        ReportExecutionRequestDto request,
        CancellationToken ct);

    Task<ReportSheetDto> ExecuteExportSheetAsync(
        string reportCode,
        ReportExportRequestDto request,
        CancellationToken ct);
}
