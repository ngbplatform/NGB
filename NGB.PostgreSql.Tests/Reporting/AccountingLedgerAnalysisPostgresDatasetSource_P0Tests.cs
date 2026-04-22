using FluentAssertions;
using NGB.Contracts.Reporting;
using NGB.PostgreSql.Reporting.Accounting;
using Xunit;

namespace NGB.PostgreSql.Tests.Reporting;

public sealed class AccountingLedgerAnalysisPostgresDatasetSource_P0Tests
{
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
