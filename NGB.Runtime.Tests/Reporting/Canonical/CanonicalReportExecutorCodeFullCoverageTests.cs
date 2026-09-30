using FluentAssertions;
using NGB.Core.Reporting;
using NGB.Runtime.Reporting.Canonical;
using Xunit;

namespace NGB.Runtime.Tests.Reporting.Canonical;

public sealed class CanonicalReportExecutorCodeFullCoverageTests
{
    [Fact]
    public void Executors_ExposeTheirCanonicalReportCodes()
    {
        new AccountCardCanonicalReportExecutor(null!, null!, null!).ReportCode.Should().Be(AccountingReportCodes.AccountCard);
        new BalanceSheetCanonicalReportExecutor(null!).ReportCode.Should().Be(AccountingReportCodes.BalanceSheet);
        new GeneralJournalCanonicalReportExecutor(null!, null!, null!).ReportCode.Should().Be(AccountingReportCodes.GeneralJournal);
        new GeneralLedgerAggregatedCanonicalReportExecutor(null!, null!, null!).ReportCode.Should().Be(AccountingReportCodes.GeneralLedgerAggregated);
        new IncomeStatementCanonicalReportExecutor(null!).ReportCode.Should().Be(AccountingReportCodes.IncomeStatement);
        new StatementOfChangesInEquityCanonicalReportExecutor(null!).ReportCode.Should().Be(AccountingReportCodes.StatementOfChangesInEquity);
        new TrialBalanceCanonicalReportExecutor(null!).ReportCode.Should().Be(AccountingReportCodes.TrialBalance);
    }
}
