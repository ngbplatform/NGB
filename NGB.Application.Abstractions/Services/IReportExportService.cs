using NGB.Contracts.Reporting;

namespace NGB.Application.Abstractions.Services;

public interface IReportExportService
{
    Task<byte[]> ExportXlsxAsync(ReportSheetDto sheet, string? worksheetTitle, CancellationToken ct);
}
