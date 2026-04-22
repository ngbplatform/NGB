using FluentAssertions;
using NGB.Accounting.Accounts;
using NGB.Accounting.Reports.IncomeStatement;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Reporting;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class IncomeStatementReportService_P0Tests
{
    [Fact]
    public async Task GetAsync_Computes_ExtendedSectionTotals_And_Excludes_ZeroLines_When_Requested()
    {
        var service = new IncomeStatementReportService(
            new StubIncomeStatementSnapshotReader(
                new IncomeStatementSnapshot([
                    new IncomeStatementSnapshotRow(Guid.NewGuid(), "4000", "Rental Income", StatementSection.Income, 0m, 100m),
                    new IncomeStatementSnapshotRow(Guid.NewGuid(), "5000", "Cost of Goods Sold", StatementSection.CostOfGoodsSold, 60m, 0m),
                    new IncomeStatementSnapshotRow(Guid.NewGuid(), "6000", "Operating Expense", StatementSection.Expenses, 0m, 0m),
                    new IncomeStatementSnapshotRow(Guid.NewGuid(), "7000", "Other Income", StatementSection.OtherIncome, 0m, 10m),
                    new IncomeStatementSnapshotRow(Guid.NewGuid(), "8000", "Other Expense", StatementSection.OtherExpense, 5m, 0m)
                ])));

        var report = await service.GetAsync(
            new IncomeStatementReportRequest
            {
                FromInclusive = new DateOnly(2026, 3, 1),
                ToInclusive = new DateOnly(2026, 3, 1),
                IncludeZeroLines = false
            },
            CancellationToken.None);

        report.TotalIncome.Should().Be(110m);
        report.TotalExpenses.Should().Be(65m);
        report.TotalOtherIncome.Should().Be(10m);
        report.TotalOtherExpense.Should().Be(5m);
        report.NetIncome.Should().Be(45m);

        report.Sections.Select(x => x.Section).Should().ContainInOrder(
            StatementSection.Income,
            StatementSection.CostOfGoodsSold,
            StatementSection.OtherIncome,
            StatementSection.OtherExpense);

        report.Sections.Should().NotContain(x => x.Section == StatementSection.Expenses);
        report.Sections.SelectMany(x => x.Lines).Select(x => x.AccountCode).Should().NotContain("6000");
    }

    [Fact]
    public async Task GetAsync_IncludeZeroLines_True_Keeps_ZeroActivityRows()
    {
        var service = new IncomeStatementReportService(
            new StubIncomeStatementSnapshotReader(
                new IncomeStatementSnapshot([
                    new IncomeStatementSnapshotRow(Guid.NewGuid(), "4000", "Rental Income", StatementSection.Income, 0m, 0m),
                    new IncomeStatementSnapshotRow(Guid.NewGuid(), "6000", "Operating Expense", StatementSection.Expenses, 0m, 0m)
                ])));

        var report = await service.GetAsync(
            new IncomeStatementReportRequest
            {
                FromInclusive = new DateOnly(2026, 3, 1),
                ToInclusive = new DateOnly(2026, 3, 1),
                IncludeZeroLines = true
            },
            CancellationToken.None);

        report.Sections.Should().HaveCount(5);
        report.Sections.SelectMany(x => x.Lines).Should().HaveCount(2);
        report.Sections.SelectMany(x => x.Lines).Select(x => x.Amount).Should().OnlyContain(x => x == 0m);
        report.NetIncome.Should().Be(0m);
    }

    [Fact]
    public async Task GetAsync_WhenSnapshotContainsNonProfitAndLossSection_ThrowsInvariant()
    {
        var service = new IncomeStatementReportService(
            new StubIncomeStatementSnapshotReader(
                new IncomeStatementSnapshot([
                    new IncomeStatementSnapshotRow(Guid.NewGuid(), "1000", "Cash", StatementSection.Assets, 1m, 0m)
                ])));

        var act = () => service.GetAsync(
            new IncomeStatementReportRequest
            {
                FromInclusive = new DateOnly(2026, 3, 1),
                ToInclusive = new DateOnly(2026, 3, 1),
                IncludeZeroLines = false
            },
            CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>();
    }

    private sealed class StubIncomeStatementSnapshotReader(IncomeStatementSnapshot snapshot) : IIncomeStatementSnapshotReader
    {
        public Task<IncomeStatementSnapshot> GetAsync(
            DateOnly fromInclusive,
            DateOnly toInclusive,
            NGB.Core.Dimensions.DimensionScopeBag? dimensionScopes,
            bool includeZeroLines,
            CancellationToken ct = default)
            => Task.FromResult(snapshot);
    }
}
