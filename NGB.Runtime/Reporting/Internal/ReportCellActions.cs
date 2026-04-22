using System.Text.Json;
using NGB.Contracts.Reporting;

namespace NGB.Runtime.Reporting.Internal;

public static class ReportCellActions
{
    public static ReportCellActionDto BuildDocumentAction(string documentType, Guid documentId)
        => new(Kind: ReportCellActionKinds.OpenDocument, DocumentType: documentType, DocumentId: documentId);

    public static ReportCellActionDto BuildAccountAction(Guid accountId)
        => new(Kind: ReportCellActionKinds.OpenAccount, AccountId: accountId);

    public static ReportCellActionDto BuildCatalogAction(string catalogType, Guid id)
        => new(Kind: ReportCellActionKinds.OpenCatalog, CatalogType: catalogType, CatalogId: id);

    public static ReportCellActionDto BuildReportAction(
        string reportCode,
        IReadOnlyDictionary<string, string>? parameters = null,
        IReadOnlyDictionary<string, ReportFilterValueDto>? filters = null)
        => new(
            Kind: ReportCellActionKinds.OpenReport,
            Report: new ReportCellReportTargetDto(reportCode, parameters, filters));

    public static ReportCellActionDto BuildAccountCardAction(
        Guid accountId,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        IReadOnlyDictionary<string, ReportFilterValueDto>? inheritedFilters = null)
    {
        var filters = new Dictionary<string, ReportFilterValueDto>(StringComparer.OrdinalIgnoreCase);
        if (inheritedFilters is not null)
        {
            foreach (var pair in inheritedFilters)
            {
                filters[pair.Key] = pair.Value;
            }
        }

        filters["account_id"] = new ReportFilterValueDto(JsonSerializer.SerializeToElement(accountId));

        return BuildReportAction(
            reportCode: "accounting.account_card",
            parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["from_utc"] = fromInclusive.ToString("yyyy-MM-dd"),
                ["to_utc"] = toInclusive.ToString("yyyy-MM-dd")
            },
            filters: filters);
    }
}
