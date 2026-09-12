using NGB.Contracts.Reporting;

namespace NGB.Application.Abstractions.Services;

public interface IStreamingReportExportService
{
    Task WriteXlsxAsync(
        Stream output,
        ReportSheetDto template,
        IAsyncEnumerable<ReportSheetRowDto> rows,
        CancellationToken ct);
}
