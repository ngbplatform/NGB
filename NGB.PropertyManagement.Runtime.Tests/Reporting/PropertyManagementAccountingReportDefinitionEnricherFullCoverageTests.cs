using FluentAssertions;
using NGB.Contracts.Reporting;
using NGB.Core.Reporting;
using NGB.PropertyManagement.Runtime.Reporting;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PropertyManagement.Runtime.Tests.Reporting;

public sealed class PropertyManagementAccountingReportDefinitionEnricherFullCoverageTests
{
    private readonly PropertyManagementAccountingReportDefinitionEnricher _sut = new();

    [Fact]
    public void Null_definition_is_rejected_and_unsupported_definition_is_preserved()
    {
        ((Action)(() => _sut.Enrich(null!))).Should().Throw<NgbInvariantViolationException>();

        var unsupported = new ReportDefinitionDto("custom.report", "Custom");
        _sut.Enrich(unsupported).Should().BeSameAs(unsupported);
    }

    [Theory]
    [InlineData(AccountingReportCodes.TrialBalance)]
    [InlineData(AccountingReportCodes.GeneralJournal)]
    [InlineData(AccountingReportCodes.AccountCard)]
    [InlineData(AccountingReportCodes.GeneralLedgerAggregated)]
    public void Supported_accounting_reports_receive_all_pm_filters(string reportCode)
    {
        var dataset = new ReportDatasetDto("original");
        var definition = new ReportDefinitionDto(reportCode, "Accounting", Dataset: dataset);

        var result = _sut.Enrich(definition);

        result.Should().NotBeSameAs(definition);
        result.Dataset.Should().BeSameAs(dataset);
        result.Filters.Should().NotBeNull();
        result.Filters!.Select(x => x.FieldCode).Should().Equal("property_id", "lease_id", "party_id");
        result.Filters[0].SupportsIncludeDescendants.Should().BeTrue();
        result.Filters[0].DefaultIncludeDescendants.Should().BeTrue();
        result.Filters.Should().OnlyContain(x => x.IsMulti);
    }

    [Fact]
    public void Existing_filters_are_replaced_case_insensitively_and_unrelated_filters_are_preserved()
    {
        var definition = new ReportDefinitionDto(
            AccountingReportCodes.GeneralJournal,
            "Journal",
            Filters:
            [
                new ReportFilterFieldDto("custom", "Custom", "string"),
                new ReportFilterFieldDto("PROPERTY_ID", "Old property", "string"),
                new ReportFilterFieldDto("lease_id", "Old lease", "string"),
                new ReportFilterFieldDto("PARTY_ID", "Old party", "string")
            ]);

        var result = _sut.Enrich(definition);

        result.Filters.Should().HaveCount(4);
        result.Filters![0].FieldCode.Should().Be("custom");
        result.Filters.Skip(1).Select(x => x.DataType).Should().OnlyContain(x => x == "uuid");
    }

    [Fact]
    public void Ledger_analysis_preserves_null_dataset()
    {
        var result = _sut.Enrich(new ReportDefinitionDto(
            AccountingReportCodes.LedgerAnalysis,
            "Ledger analysis"));

        result.Dataset.Should().BeNull();
        result.Filters.Should().HaveCount(3);
    }

    [Fact]
    public void Ledger_analysis_builds_pm_dataset_fields_from_empty_field_collection()
    {
        var definition = new ReportDefinitionDto(
            AccountingReportCodes.LedgerAnalysis,
            "Ledger analysis",
            Dataset: new ReportDatasetDto("accounting.ledger.analysis", Fields: null));

        var result = _sut.Enrich(definition);

        result.Dataset!.DatasetCode.Should().Be("pm.accounting.ledger.analysis");
        result.Dataset.Fields!.Select(x => x.Code).Should().Equal("property_id", "lease_id", "party_id");
        result.Dataset.Fields.Should().OnlyContain(x => x.Kind == ReportFieldKind.Dimension && x.IsFilterable);
    }

    [Fact]
    public void Ledger_analysis_replaces_existing_pm_fields_and_preserves_custom_fields_and_measures()
    {
        var custom = new ReportFieldDto("custom", "Custom", "string", ReportFieldKind.Attribute);
        var measure = new ReportMeasureDto("amount", "Amount", "decimal");
        var dataset = new ReportDatasetDto(
            "old",
            [
                custom,
                new ReportFieldDto("PROPERTY_ID", "Old", "string", ReportFieldKind.Attribute),
                new ReportFieldDto("lease_id", "Old", "string", ReportFieldKind.Attribute),
                new ReportFieldDto("PARTY_ID", "Old", "string", ReportFieldKind.Attribute)
            ],
            [measure]);

        var result = _sut.Enrich(new ReportDefinitionDto(
            AccountingReportCodes.LedgerAnalysis,
            "Ledger analysis",
            Dataset: dataset));

        result.Dataset!.Fields.Should().HaveCount(4);
        result.Dataset.Fields![0].Should().BeSameAs(custom);
        result.Dataset.Fields.Skip(1).Should().OnlyContain(x => x.Kind == ReportFieldKind.Dimension);
        result.Dataset.Measures.Should().ContainSingle().Which.Should().BeSameAs(measure);
    }
}
