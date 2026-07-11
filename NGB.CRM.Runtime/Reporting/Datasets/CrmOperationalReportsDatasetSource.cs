using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;

namespace NGB.CRM.Runtime.Reporting.Datasets;

public sealed class CrmOperationalReportsDatasetSource : IReportDatasetSource
{
    public IReadOnlyList<ReportDatasetDto> GetDatasets()
        =>
        [
            CrmPipelineDatasetModel.Create(),
            CrmOpportunityHistoryDatasetModel.Create(),
            CrmLeadFunnelDatasetModel.Create(),
            CrmActivitySummaryDatasetModel.Create(),
            CrmQuoteRegisterDatasetModel.Create()
        ];
}
