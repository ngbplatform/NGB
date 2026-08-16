using System.Text.Json;
using FluentAssertions;
using NGB.Accounting.Reports.AccountCard;
using NGB.Accounting.Reports.CashFlowIndirect;
using NGB.Accounting.Reports.LedgerAnalysis;
using NGB.Accounting.Reports.StatementOfChangesInEquity;
using NGB.Accounting.Reports.TrialBalance;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Accounting.Tests.Reports;

public sealed class AccountingReportModelsTests
{
    [Fact]
    public void AccountCardGroupedReport_PropertiesRoundTrip()
    {
        var accountId = Guid.CreateVersion7();
        var from = new DateOnly(2026, 1, 1);
        var to = new DateOnly(2026, 2, 28);
        AccountCardReportSection[] sections = [new()];
        var report = new AccountCardGroupedReport
        {
            AccountId = accountId,
            AccountCode = "1010",
            DimensionScopes = null,
            FromInclusive = from,
            ToInclusive = to,
            Grouping = AccountCardGrouping.ByMonth,
            OpeningBalance = 10m,
            TotalDebit = 20m,
            TotalCredit = 5m,
            ClosingBalance = 25m,
            Sections = sections
        };

        report.AccountId.Should().Be(accountId);
        report.AccountCode.Should().Be("1010");
        report.DimensionScopes.Should().BeNull();
        report.FromInclusive.Should().Be(from);
        report.ToInclusive.Should().Be(to);
        report.Grouping.Should().Be(AccountCardGrouping.ByMonth);
        report.OpeningBalance.Should().Be(10m);
        report.TotalDebit.Should().Be(20m);
        report.TotalCredit.Should().Be(5m);
        report.ClosingBalance.Should().Be(25m);
        report.Sections.Should().BeSameAs(sections);
    }

    [Fact]
    public void AccountCardReportSection_PropertiesRoundTrip()
    {
        var first = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var last = first.AddDays(1);
        AccountCardReportLine[] lines = [new()];
        var section = new AccountCardReportSection
        {
            Title = "January",
            FirstPeriodUtc = first,
            LastPeriodUtc = last,
            TotalDebit = 20m,
            TotalCredit = 5m,
            Delta = 15m,
            ClosingBalance = 25m,
            Lines = lines
        };

        section.Title.Should().Be("January");
        section.FirstPeriodUtc.Should().Be(first);
        section.LastPeriodUtc.Should().Be(last);
        section.TotalDebit.Should().Be(20m);
        section.TotalCredit.Should().Be(5m);
        section.Delta.Should().Be(15m);
        section.ClosingBalance.Should().Be(25m);
        section.Lines.Should().BeSameAs(lines);
    }

    [Fact]
    public void LedgerAnalysisSelections_AllConstructorPropertiesAreReadable()
    {
        var field = new LedgerAnalysisFlatDetailFieldSelection("account", "account", "Account", "string");
        var measure = new LedgerAnalysisFlatDetailMeasureSelection("amount", "amount", "Amount", "decimal");
        var value = JsonSerializer.SerializeToElement("1010");
        var predicate = new LedgerAnalysisFlatDetailPredicate("account", "account", "Account", "string", value);

        field.FieldCode.Should().Be("account");
        field.OutputCode.Should().Be("account");
        field.Label.Should().Be("Account");
        field.DataType.Should().Be("string");
        measure.MeasureCode.Should().Be("amount");
        measure.OutputCode.Should().Be("amount");
        measure.Label.Should().Be("Amount");
        measure.DataType.Should().Be("decimal");
        predicate.FieldCode.Should().Be("account");
        predicate.OutputCode.Should().Be("account");
        predicate.Label.Should().Be("Account");
        predicate.DataType.Should().Be("string");
        predicate.Value.GetString().Should().Be("1010");
    }

    [Fact]
    public void TrialBalanceReportTotals_AllConstructorPropertiesAreReadable()
    {
        var totals = new TrialBalanceReportTotals(1m, 2m, 3m, 4m);

        totals.OpeningBalance.Should().Be(1m);
        totals.DebitAmount.Should().Be(2m);
        totals.CreditAmount.Should().Be(3m);
        totals.ClosingBalance.Should().Be(4m);
    }

    [Fact]
    public void CashFlowIndirectRequest_ReversedRange_Throws()
    {
        var request = new CashFlowIndirectReportRequest
        {
            FromInclusive = new DateOnly(2026, 2, 1),
            ToInclusive = new DateOnly(2026, 1, 31)
        };

        Action act = request.Validate;

        act.Should().Throw<NgbArgumentOutOfRangeException>();
    }

    [Fact]
    public void CashFlowIndirectRequest_EqualBounds_IsValid()
    {
        var date = new DateOnly(2026, 2, 1);
        var request = new CashFlowIndirectReportRequest
        {
            FromInclusive = date,
            ToInclusive = date
        };

        Action act = request.Validate;

        act.Should().NotThrow();
    }

    [Fact]
    public void StatementOfChangesInEquityRequest_ReversedMonthRange_Throws()
    {
        var request = new StatementOfChangesInEquityReportRequest
        {
            FromInclusive = new DateOnly(2026, 2, 1),
            ToInclusive = new DateOnly(2026, 1, 1)
        };

        Action act = request.Validate;

        act.Should().Throw<NgbArgumentOutOfRangeException>();
    }

    [Fact]
    public void StatementOfChangesInEquityRequest_EqualMonthBounds_IsValid()
    {
        var month = new DateOnly(2026, 2, 1);
        var request = new StatementOfChangesInEquityReportRequest
        {
            FromInclusive = month,
            ToInclusive = month
        };

        Action act = request.Validate;

        act.Should().NotThrow();
    }
}
