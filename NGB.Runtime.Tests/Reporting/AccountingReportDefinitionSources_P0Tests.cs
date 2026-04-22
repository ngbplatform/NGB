using FluentAssertions;
using NGB.Runtime.Reporting.Definitions;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class AccountingReportDefinitionSources_P0Tests
{
    [Fact]
    public void Canonical_accounting_definitions_do_not_embed_pm_specific_filters_or_lookups()
    {
        var definitions = new CanonicalAccountingReportDefinitionSource().GetDefinitions();

        definitions.SelectMany(x => x.Filters ?? []).Select(x => x.FieldCode)
            .Should().NotContain(new[] { "property_id", "party_id", "lease_id" });

        definitions
            .SelectMany(x => x.Filters ?? [])
            .Select(x => x.Lookup)
            .Where(x => x is not null)
            .Should().AllSatisfy(lookup => lookup.Should().NotBeOfType<NGB.Contracts.Metadata.CatalogLookupSourceDto>());

        definitions
            .SelectMany(x => x.Filters ?? [])
            .Select(x => x.Lookup)
            .OfType<NGB.Contracts.Metadata.DocumentLookupSourceDto>()
            .SelectMany(x => x.DocumentTypes)
            .Should().NotContain(x => x.StartsWith("pm.", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void User_facing_report_parameters_use_labels_without_legacy_technical_date_descriptions()
    {
        var canonicalDefinitions = new CanonicalAccountingReportDefinitionSource().GetDefinitions();
        canonicalDefinitions
            .SelectMany(x => x.Parameters ?? [])
            .Should().OnlyContain(x => !string.IsNullOrWhiteSpace(x.Label));

        var ledgerAnalysis = new AccountingLedgerAnalysisDefinitionSource().GetDefinitions().Single();
        ledgerAnalysis.Parameters.Should().NotBeNull();
        ledgerAnalysis.Parameters!
            .Select(x => new { x.Code, x.Label, x.Description })
            .Should().ContainEquivalentOf(new { Code = "from_utc", Label = "From", Description = (string?)null })
            .And.ContainEquivalentOf(new { Code = "to_utc", Label = "To", Description = (string?)null });
    }
    
    [Fact]
    public void Balance_sheet_definition_exposes_only_as_of_parameter()
    {
        var definition = new CanonicalAccountingReportDefinitionSource().GetDefinitions()
            .Single(x => x.ReportCode == "accounting.balance_sheet");

        definition.Parameters.Should().NotBeNull();
        definition.Parameters!
            .Select(x => new { x.Code, x.Label, x.Description })
            .Should().Equal(new { Code = "as_of_utc", Label = (string?)"As of", Description = (string?)null });
    }

    [Fact]
    public void Statement_of_changes_in_equity_definition_exposes_canonical_date_range_without_filters()
    {
        var definition = new CanonicalAccountingReportDefinitionSource().GetDefinitions()
            .Single(x => x.ReportCode == "accounting.statement_of_changes_in_equity");

        definition.Parameters.Should().NotBeNull();
        definition.Parameters!
            .Select(x => new { x.Code, x.Label, x.Description })
            .Should().Equal(
                new { Code = "from_utc", Label = (string?)"From", Description = (string?)null },
                new { Code = "to_utc", Label = (string?)"To", Description = (string?)null });

        definition.Filters.Should().BeEmpty();
        definition.Presentation.Should().BeNull();
        definition.Capabilities!.AllowsGrandTotals.Should().BeTrue();
        definition.Capabilities.AllowsSubtotals.Should().BeFalse();
    }

    [Fact]
    public void Cash_flow_indirect_definition_is_bounded_filterless_and_without_paged_presentation_metadata()
    {
        var definition = new CanonicalAccountingReportDefinitionSource().GetDefinitions()
            .Single(x => x.ReportCode == "accounting.cash_flow_statement_indirect");

        definition.Parameters.Should().NotBeNull();
        definition.Parameters!
            .Select(x => new { x.Code, x.Label, x.Description })
            .Should().Equal(
                new { Code = "from_utc", Label = (string?)"From", Description = (string?)null },
                new { Code = "to_utc", Label = (string?)"To", Description = (string?)null });

        definition.Filters.Should().BeEmpty();
        definition.Presentation.Should().BeNull();
        definition.Capabilities.Should().NotBeNull();
        definition.Capabilities!.AllowsFilters.Should().BeFalse();
        definition.Capabilities.AllowsGrandTotals.Should().BeFalse();
        definition.Capabilities.AllowsSubtotals.Should().BeFalse();
    }

    [Fact]
    public void Paged_canonical_accounting_reports_publish_backend_owned_presentation_hints()
    {
        var definitions = new CanonicalAccountingReportDefinitionSource().GetDefinitions();

        var generalJournal = definitions.Single(x => x.ReportCode == "accounting.general_journal");
        generalJournal.Presentation.Should().BeEquivalentTo(new NGB.Contracts.Reporting.ReportPresentationDto(
            InitialPageSize: 200,
            RowNoun: "journal line",
            EmptyStateMessage: null));

        var accountCard = definitions.Single(x => x.ReportCode == "accounting.account_card");
        accountCard.Presentation.Should().BeEquivalentTo(new NGB.Contracts.Reporting.ReportPresentationDto(
            InitialPageSize: 150,
            RowNoun: "account card line",
            EmptyStateMessage: "Open the Composer, choose an account and period, and run again."));

        var generalLedgerAggregated = definitions.Single(x => x.ReportCode == "accounting.general_ledger_aggregated");
        generalLedgerAggregated.Presentation.Should().BeEquivalentTo(new NGB.Contracts.Reporting.ReportPresentationDto(
            InitialPageSize: 100,
            RowNoun: "ledger line",
            EmptyStateMessage: "Open the Composer, choose an account and period, and run again."));

        var postingLog = definitions.Single(x => x.ReportCode == "accounting.posting_log");
        postingLog.Presentation.Should().BeEquivalentTo(new NGB.Contracts.Reporting.ReportPresentationDto(
            InitialPageSize: 100,
            RowNoun: "posting operation",
            EmptyStateMessage: "Adjust the time window or filters and run again."));

        definitions
            .Where(x => x.ReportCode is not "accounting.general_journal" and not "accounting.account_card" and not "accounting.general_ledger_aggregated" and not "accounting.posting_log")
            .Should().OnlyContain(x => x.Presentation == null);
    }

    [Fact]
    public void Ledger_analysis_definition_is_platform_neutral_at_definition_level()
    {
        var definition = new AccountingLedgerAnalysisDefinitionSource().GetDefinitions().Single();

        definition.Description.Should().Be("Composable accounting ledger analysis");

        var filters = definition.Filters;
        filters.Should().NotBeNull();
        filters!.Select(x => x.FieldCode).Should().Equal("account_id");
        filters.Single().Lookup.Should().BeOfType<NGB.Contracts.Metadata.ChartOfAccountsLookupSourceDto>();
    }

    [Fact]
    public void Ledger_analysis_dataset_exposes_only_canonical_user_facing_grouping_detail_and_sort_fields()
    {
        var definition = new AccountingLedgerAnalysisDefinitionSource().GetDefinitions().Single();
        definition.Dataset.Should().NotBeNull();
        var dataset = definition.Dataset!;

        dataset.Fields!.Where(x => x.IsGroupable == true).Select(x => x.Label)
            .Should().Equal("Period", "Account", "Document");

        dataset.Fields!.Where(x => x.IsSelectable == true).Select(x => x.Label)
            .Should().Equal("Period", "Account", "Document");

        dataset.Fields!.Where(x => x.IsSortable == true).Select(x => x.Label)
            .Should().Equal("Period", "Account", "Document");

        dataset.Fields!.Where(x => x.IsFilterable == true).Select(x => x.Code)
            .Should().Equal("account_id");

        dataset.Measures!.Select(x => x.Label).Should().Equal("Debit", "Credit", "Net");
        dataset.Measures!.Single(x => x.Code == "debit_amount").SupportedAggregations
            .Should().BeEquivalentTo([NGB.Contracts.Reporting.ReportAggregationKind.Sum, NGB.Contracts.Reporting.ReportAggregationKind.Min, NGB.Contracts.Reporting.ReportAggregationKind.Max, NGB.Contracts.Reporting.ReportAggregationKind.Average], options => options.WithStrictOrdering());
    }

    [Fact]
    public void Ledger_analysis_definition_uses_explicit_user_facing_default_layout_values()
    {
        var definition = new AccountingLedgerAnalysisDefinitionSource().GetDefinitions().Single();

        definition.DefaultLayout.Should().NotBeNull();
        definition.DefaultLayout!.RowGroups.Should().Equal(
            new NGB.Contracts.Reporting.ReportGroupingDto("account_display"),
            new NGB.Contracts.Reporting.ReportGroupingDto("period_utc"));

        definition.DefaultLayout.Measures.Should().Equal(
            new NGB.Contracts.Reporting.ReportMeasureSelectionDto("debit_amount", NGB.Contracts.Reporting.ReportAggregationKind.Sum),
            new NGB.Contracts.Reporting.ReportMeasureSelectionDto("credit_amount", NGB.Contracts.Reporting.ReportAggregationKind.Sum),
            new NGB.Contracts.Reporting.ReportMeasureSelectionDto("net_amount", NGB.Contracts.Reporting.ReportAggregationKind.Sum));

        definition.DefaultLayout.Sorts.Should().Equal(
            new NGB.Contracts.Reporting.ReportSortDto("account_display", NGB.Contracts.Reporting.ReportSortDirection.Asc),
            new NGB.Contracts.Reporting.ReportSortDto("period_utc", NGB.Contracts.Reporting.ReportSortDirection.Asc));
    }

    [Fact]
    public void Separate_row_subtotals_capability_is_exposed_only_for_reports_that_allow_that_presentation_mode()
    {
        var canonicalDefinitions = new CanonicalAccountingReportDefinitionSource().GetDefinitions();

        canonicalDefinitions
            .Where(x => x.ReportCode is "accounting.trial_balance" or "accounting.balance_sheet" or "accounting.income_statement")
            .Should().OnlyContain(x => x.Capabilities != null && x.Capabilities.AllowsSubtotals && !x.Capabilities.AllowsSeparateRowSubtotals);

        var ledgerAnalysis = new AccountingLedgerAnalysisDefinitionSource().GetDefinitions().Single();
        ledgerAnalysis.Capabilities.Should().NotBeNull();
        ledgerAnalysis.Capabilities!.AllowsSeparateRowSubtotals.Should().BeTrue();
        ledgerAnalysis.DefaultLayout!.ShowSubtotalsOnSeparateRows.Should().BeTrue();
    }

    [Fact]
    public void Posting_log_definition_uses_backend_owned_enum_options_with_user_friendly_labels()
    {
        var definition = new CanonicalAccountingReportDefinitionSource().GetDefinitions()
            .Single(x => x.ReportCode == "accounting.posting_log");

        definition.Description.Should().Be("Posting engine activity log for diagnostics and support");
        definition.Capabilities.Should().NotBeNull();
        definition.Capabilities!.AllowsGrandTotals.Should().BeFalse();
        definition.DefaultLayout.Should().NotBeNull();
        definition.DefaultLayout!.ShowGrandTotals.Should().BeFalse();
        definition.Filters!.Select(x => x.FieldCode).Should().Equal("operation", "status");

        definition.Filters!.Single(x => x.FieldCode == "operation").Options.Should().BeEquivalentTo(
        [
            new NGB.Contracts.Reporting.ReportFilterOptionDto("Post", "Post"),
            new NGB.Contracts.Reporting.ReportFilterOptionDto("Unpost", "Unpost"),
            new NGB.Contracts.Reporting.ReportFilterOptionDto("Repost", "Repost"),
            new NGB.Contracts.Reporting.ReportFilterOptionDto("CloseFiscalYear", "Close fiscal year")
        ]);

        definition.Filters!.Single(x => x.FieldCode == "status").Options.Should().BeEquivalentTo(
        [
            new NGB.Contracts.Reporting.ReportFilterOptionDto("InProgress", "In progress"),
            new NGB.Contracts.Reporting.ReportFilterOptionDto("Completed", "Completed"),
            new NGB.Contracts.Reporting.ReportFilterOptionDto("StaleInProgress", "Stale in progress")
        ]);
    }
}
