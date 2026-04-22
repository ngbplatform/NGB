using NGB.Application.Abstractions.Services;
using NGB.Contracts.Metadata;
using NGB.Contracts.Reporting;
using NGB.Tools.Exceptions;

namespace NGB.PropertyManagement.Runtime.Reporting;

public sealed class PropertyManagementAccountingReportDefinitionEnricher : IReportDefinitionEnricher
{
    private const string LedgerAnalysisReportCode = "accounting.ledger.analysis";
    private const string PmLedgerAnalysisDatasetCode = "pm.accounting.ledger.analysis";

    private static readonly HashSet<string> SupportedReportCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "accounting.trial_balance",
        "accounting.general_journal",
        "accounting.account_card",
        "accounting.general_ledger_aggregated",
        LedgerAnalysisReportCode
    };

    public ReportDefinitionDto Enrich(ReportDefinitionDto definition)
    {
        if (definition is null)
            throw new NgbInvariantViolationException("PM accounting report definition enricher received null definition.");

        if (!SupportedReportCodes.Contains(definition.ReportCode))
            return definition;

        var filters = (definition.Filters ?? []).ToList();
        var dataset = definition.Dataset;

        Upsert(filters, CreatePropertyFilter());
        Upsert(filters, CreateLeaseFilter());
        Upsert(filters, CreatePartyFilter());

        if (string.Equals(definition.ReportCode, LedgerAnalysisReportCode, StringComparison.OrdinalIgnoreCase))
            dataset = BuildPmLedgerAnalysisDataset(definition.Dataset);

        return definition with
        {
            Filters = filters,
            Dataset = dataset
        };
    }

    private static ReportFilterFieldDto CreatePropertyFilter()
        => new(
            "property_id",
            "Property",
            "uuid",
            IsMulti: true,
            SupportsIncludeDescendants: true,
            DefaultIncludeDescendants: true,
            Lookup: new CatalogLookupSourceDto(PropertyManagementCodes.Property),
            Description: "Select one or more properties. Building selections can include child units");

    private static ReportFilterFieldDto CreateLeaseFilter()
        => new(
            "lease_id",
            "Lease",
            "uuid",
            IsMulti: true,
            Lookup: new DocumentLookupSourceDto([PropertyManagementCodes.Lease]));

    private static ReportFilterFieldDto CreatePartyFilter()
        => new(
            "party_id",
            "Party",
            "uuid",
            IsMulti: true,
            Lookup: new CatalogLookupSourceDto(PropertyManagementCodes.Party));

    private static ReportDatasetDto? BuildPmLedgerAnalysisDataset(ReportDatasetDto? dataset)
    {
        if (dataset is null)
            return null;

        var fields = (dataset.Fields ?? []).ToList();
        Upsert(fields, CreatePropertyDatasetField());
        Upsert(fields, CreateLeaseDatasetField());
        Upsert(fields, CreatePartyDatasetField());

        return dataset with
        {
            DatasetCode = PmLedgerAnalysisDatasetCode,
            Fields = fields
        };
    }

    private static ReportFieldDto CreatePropertyDatasetField()
        => new(
            "property_id",
            "Property",
            "uuid",
            ReportFieldKind.Dimension,
            IsFilterable: true,
            SupportsIncludeDescendants: true,
            DefaultIncludeDescendants: true);

    private static ReportFieldDto CreateLeaseDatasetField()
        => new(
            "lease_id",
            "Lease",
            "uuid",
            ReportFieldKind.Dimension,
            IsFilterable: true);

    private static ReportFieldDto CreatePartyDatasetField()
        => new(
            "party_id",
            "Party",
            "uuid",
            ReportFieldKind.Dimension,
            IsFilterable: true);

    private static void Upsert(IList<ReportFilterFieldDto> filters, ReportFilterFieldDto filter)
    {
        for (var i = 0; i < filters.Count; i++)
        {
            if (!string.Equals(filters[i].FieldCode, filter.FieldCode, StringComparison.OrdinalIgnoreCase))
                continue;

            filters[i] = filter;
            return;
        }

        filters.Add(filter);
    }

    private static void Upsert(IList<ReportFieldDto> fields, ReportFieldDto field)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            if (!string.Equals(fields[i].Code, field.Code, StringComparison.OrdinalIgnoreCase))
                continue;

            fields[i] = field;
            return;
        }

        fields.Add(field);
    }
}
