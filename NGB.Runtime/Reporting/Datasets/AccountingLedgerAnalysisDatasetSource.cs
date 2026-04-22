using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;

namespace NGB.Runtime.Reporting.Datasets;

public sealed class AccountingLedgerAnalysisDatasetSource : IReportDatasetSource
{
    public IReadOnlyList<ReportDatasetDto> GetDatasets() => [AccountingLedgerAnalysisDatasetModel.Create()];
}
