using NGB.Contracts.Reporting;

namespace NGB.Application.Abstractions.Services;

public interface IReportDatasetCatalog
{
    Task<IReadOnlyList<ReportDatasetDto>> GetAllDatasetsAsync(CancellationToken ct);
    Task<ReportDatasetDto> GetDatasetAsync(string datasetCode, CancellationToken ct);
}
