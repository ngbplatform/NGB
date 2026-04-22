using NGB.Contracts.Reporting;

namespace NGB.Application.Abstractions.Services;

public interface IReportDatasetSource
{
    IReadOnlyList<ReportDatasetDto> GetDatasets();
}
