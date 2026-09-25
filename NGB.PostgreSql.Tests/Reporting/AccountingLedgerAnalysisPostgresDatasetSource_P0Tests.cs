using FluentAssertions;
using NGB.Contracts.Reporting;
using NGB.PostgreSql.Reporting.Accounting;
using Xunit;

namespace NGB.PostgreSql.Tests.Reporting;

public sealed class AccountingLedgerAnalysisPostgresDatasetSource_P0Tests
{
    [Fact]
    public void Optimized_source_requires_account_or_period_fields_and_additive_measures()
    {
        var request = new NGB.PostgreSql.Reporting.PostgresReportExecutionRequest("accounting.ledger.analysis", [], [], [],
            [new("debit_amount", "amount", "Amount", "decimal", ReportAggregationKind.Sum)], [], [], new Dictionary<string, object?>(), new(0, 10));
        foreach (var field in new[] { "account_id", "account_code", "account_name", "account_display", "period_utc" })
        {
            var valid = request with { RowGroups = [new(field, field, field, "string")], Sorts = [new(field, null, ReportSortDirection.Asc)],
                Selection = new([new(field)], [[System.Text.Json.JsonSerializer.SerializeToElement("A")]]) };
            AccountingLedgerAnalysisPostgresDatasetSource.SelectAccountAggregateSource(valid).Should().NotBeNull();
        }
        var invalid = new[]
        {
            request with { DetailFields = [new("account_code", "account_code", "Code", "string")] },
            request with { Measures = [] },
            request with { Measures = [new("debit_amount", "amount", "Amount", "decimal", ReportAggregationKind.Average)] },
            request with { ColumnGroups = [new("dimension_set_id", "dimension", "Dimension", "uuid")] },
            request with { Predicates = [new("document_id", "document", "Document", "uuid", new(System.Text.Json.JsonSerializer.SerializeToElement(Guid.NewGuid())))] },
            request with { Sorts = [new("document_display", null, ReportSortDirection.Asc)] },
            request with { Selection = new([new("document_id")], []) }
        };
        foreach (var item in invalid)
            AccountingLedgerAnalysisPostgresDatasetSource.SelectAccountAggregateSource(item).Should().BeNull();
        AccountingLedgerAnalysisPostgresDatasetSource.SelectAccountAggregateSource(request with { Sorts = [new("debit_amount", "debit_amount", ReportSortDirection.Desc)] }).Should().NotBeNull();
    }

    [Fact]
    public void Source_Registers_Ledger_Analysis_Dataset_Binding()
    {
        var sut = new AccountingLedgerAnalysisPostgresDatasetSource();

        var binding = sut.GetDatasets().Should().ContainSingle().Subject;

        binding.DatasetCodeNorm.Should().Be("accounting.ledger.analysis");
        binding.FromSql.Should().Contain("accounting_register_main");
        binding.BaseWhereSql.Should().Contain("@from_utc");
        binding.GetField("account_display").ResolveExpression(null).Should().Be("x.account_display");
        binding.GetMeasure("debit_amount").ResolveAggregateExpression(ReportAggregationKind.Sum).Should().Be("SUM(x.debit_amount)");
    }

    [Fact]
    public void Source_Maps_Period_Quarter_Time_Grain_To_Quarter_Bucket()
    {
        var sut = new AccountingLedgerAnalysisPostgresDatasetSource();

        var binding = sut.GetDatasets().Should().ContainSingle().Subject;

        binding.GetField("period_utc").ResolveExpression(ReportTimeGrain.Quarter).Should().Be("date_trunc('quarter', x.period)");
    }

    [Fact]
    public void Source_Does_Not_Expose_PM_Specific_Field_Bindings()
    {
        var sut = new AccountingLedgerAnalysisPostgresDatasetSource();

        var binding = sut.GetDatasets().Should().ContainSingle().Subject;
        var fieldCodes = binding.Fields.Keys;

        fieldCodes.Should().NotContain(["property_id", "party_id", "lease_id"]);
        binding.FromSql.Should().NotContain("pm.property");
        binding.FromSql.Should().NotContain("pm.party");
        binding.FromSql.Should().NotContain("pm.lease");
    }
}
