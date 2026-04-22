using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;

namespace NGB.Trade.Runtime.Reporting.Datasets;

public sealed class TradeOperationalReportsDatasetSource : IReportDatasetSource
{
    public IReadOnlyList<ReportDatasetDto> GetDatasets()
        => [TradeInventoryBalancesDatasetModel.Create(), TradeInventoryMovementsDatasetModel.Create()];
}
