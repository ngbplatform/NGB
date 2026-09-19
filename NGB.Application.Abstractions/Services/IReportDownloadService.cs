using NGB.Contracts.Reporting;

namespace NGB.Application.Abstractions.Services;

/// <summary>Prepares a validated export in the caller's access context. The caller owns its lifetime.</summary>
public interface IReportDownloadService
{
    Task<IReportDownload> PrepareAsync(string reportCode, ReportExportRequestDto request, CancellationToken ct);
}

/// <summary>A single-use, forward-only export. Disposal releases its source read session.</summary>
public interface IReportDownload : IAsyncDisposable
{
    string? Title { get; }
    Task WriteAsync(Stream destination, CancellationToken ct);
}
