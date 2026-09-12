using NGB.Contracts.Reporting;

namespace NGB.Application.Abstractions.Services;

public interface IReportRunService
{
    bool Supports(string reportCode);

    Task<ReportRunDto> StartAsync(
        string reportCode,
        string owner,
        ReportExecutionRequestDto request,
        CancellationToken ct);

    Task<ReportRunDto?> GetAsync(string reportCode, string owner, Guid id, CancellationToken ct);
    Task CancelAsync(string reportCode, string owner, Guid id, CancellationToken ct);

    Task<ReportExecutionResponseDto> ReadAsync(
        string reportCode,
        string owner,
        Guid id,
        int offset,
        int limit,
        CancellationToken ct);

    Task<ReportExecutionResponseDto> ContinueAsync(
        string reportCode,
        string owner,
        ReportExecutionRequestDto request,
        CancellationToken ct);

    Task<ReportRunDto> WaitAsync(string reportCode, string owner, Guid id, CancellationToken ct);
    Task<Stream> ExportAsync(string reportCode, string owner, Guid id, CancellationToken ct);
}
