using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;

namespace NGB.Mcp.Tools;

internal sealed class ListReportsTool(IAuthorizedReportReadService reports)
    : IMcpReadToolHandler<ReportsRequest, ReportsResult>, IMcpReadSourceProvider<ReportsRequest, ReportsResult>
{
    public ValueTask<IReadOnlyList<McpReadSource>> GetSourcesAsync(
        ReportsRequest request,
        ReportsResult result,
        CancellationToken ct)
        => ValueTask.FromResult<IReadOnlyList<McpReadSource>>(result.Items
            .Select(report => new McpReadSource(McpSourceKind.Report, report.ReportCode, null, report.Name))
            .ToArray());

    public async Task<ReportsResult> ExecuteAsync(ReportsRequest request, CancellationToken ct)
    {
        var definitions = (await reports.GetAllDefinitionsAsync(ct))
            .OrderBy(item => item.ReportCode, StringComparer.Ordinal)
            .ToArray();

        return new ReportsResult(
            definitions
                .Skip(request.Offset)
                .Take(request.Limit)
                .Select(report => new ReportSummary(report.ReportCode, report.Name, report.Group, report.Description))
                .ToArray(),
            request.Offset,
            request.Limit,
            definitions.Length > request.Offset + request.Limit);
    }
}

internal sealed class ReportDefinitionTool(IAuthorizedReportReadService reports)
    : IMcpReadToolHandler<ReportDefinitionRequest, ReportDefinitionDto>, IMcpReadSourceProvider<ReportDefinitionRequest, ReportDefinitionDto>
{
    public ValueTask<IReadOnlyList<McpReadSource>> GetSourcesAsync(
        ReportDefinitionRequest request,
        ReportDefinitionDto result,
        CancellationToken ct)
        => ValueTask.FromResult(ReadSources.Report(result));

    public Task<ReportDefinitionDto> ExecuteAsync(ReportDefinitionRequest request, CancellationToken ct)
        => reports.GetDefinitionAsync(request.ReportCode, ct);
}

internal sealed class ExecuteReportTool(IAuthorizedReportReadService reports, IReportRequestAdmission budget)
    : IMcpReadToolHandler<ReportReadRequest, ReportExecutionResponseDto>, IMcpReadSourceProvider<ReportReadRequest, ReportExecutionResponseDto>
{
    public async ValueTask<IReadOnlyList<McpReadSource>> GetSourcesAsync(
        ReportReadRequest request,
        ReportExecutionResponseDto result,
        CancellationToken ct)
        => ReadSources.Report(await reports.GetDefinitionAsync(request.ReportCode, ct));

    public async Task<ReportExecutionResponseDto> ExecuteAsync(ReportReadRequest request, CancellationToken ct)
    {
        if (request.Request.Limit is < 1 or > 200)
            throw new McpReadException("invalid_input", "Report page size must be between 1 and 200.");

        using var lease = await budget.AcquireAsync(ct);

        ct.ThrowIfCancellationRequested();

        if (lease is null)
            throw new McpReadException("capacity_exhausted", "Report capacity is busy. Try again shortly.");

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(budget.ExecutionTimeout);
        try
        {
            return await reports.ExecuteAsync(request.ReportCode, request.Request, deadline.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            throw new McpReadException("timeout", "The report exceeded its execution time limit.");
        }
    }
}
