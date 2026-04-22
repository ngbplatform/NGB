using FluentAssertions;
using Microsoft.Extensions.Logging;
using NGB.Accounting.CashFlow;
using NGB.Accounting.Reports.CashFlowIndirect;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Reporting;
using NGB.Runtime.Reporting.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class CashFlowIndirectReportService_P0Tests
{
    [Fact]
    public async Task GetAsync_Builds_Operating_Investing_And_Financing_Sections_And_Reconciles()
    {
        var service = new CashFlowIndirectReportService(
            new StubSnapshotReader(
                new CashFlowIndirectSnapshot(
                    NetIncome: 60m,
                    OperatingLines:
                    [
                        new CashFlowIndirectSnapshotLine(CashFlowSection.Operating, "op_wc_accounts_receivable", "Change in Accounts Receivable", 110, -25m),
                        new CashFlowIndirectSnapshotLine(CashFlowSection.Operating, "op_wc_accounts_payable", "Change in Accounts Payable", 120, 40m),
                        new CashFlowIndirectSnapshotLine(CashFlowSection.Operating, "op_adjust_depreciation_amortization", "Depreciation and amortization", 210, 15m)
                    ],
                    InvestingLines:
                    [
                        new CashFlowIndirectSnapshotLine(CashFlowSection.Investing, "inv_property_equipment_net", "Property and equipment, net", 310, -70m)
                    ],
                    FinancingLines:
                    [
                        new CashFlowIndirectSnapshotLine(CashFlowSection.Financing, "fin_debt_net", "Borrowings and repayments, net", 430, 50m)
                    ],
                    BeginningCash: 10m,
                    EndingCash: 80m,
                    BeginningLatestClosedPeriod: new DateOnly(2026, 1, 1),
                    BeginningRollForwardPeriods: 1,
                    EndingLatestClosedPeriod: new DateOnly(2026, 3, 1),
                    EndingRollForwardPeriods: 0,
                    UnclassifiedCashRows: [])),
            new SpyLogger<CashFlowIndirectReportService>());

        var report = await service.GetAsync(
            new CashFlowIndirectReportRequest
            {
                FromInclusive = new DateOnly(2026, 2, 1),
                ToInclusive = new DateOnly(2026, 3, 1)
            },
            CancellationToken.None);

        report.Sections.Select(x => x.Section)
            .Should().Equal(CashFlowSection.Operating, CashFlowSection.Investing, CashFlowSection.Financing);

        var operating = report.Sections[0];
        operating.Label.Should().Be("Operating Activities");
        operating.Lines.Select(x => x.Label)
            .Should().Equal("Net income", "Change in Accounts Receivable", "Change in Accounts Payable", "Depreciation and amortization");
        operating.Total.Should().Be(90m);

        var investing = report.Sections[1];
        investing.Total.Should().Be(-70m);

        var financing = report.Sections[2];
        financing.Total.Should().Be(50m);

        report.BeginningCash.Should().Be(10m);
        report.NetIncreaseDecreaseInCash.Should().Be(70m);
        report.EndingCash.Should().Be(80m);
    }

    [Fact]
    public async Task GetAsync_WhenSnapshotContainsUnclassifiedCashRows_ThrowsValidationException()
    {
        var service = new CashFlowIndirectReportService(
            new StubSnapshotReader(
                new CashFlowIndirectSnapshot(
                    NetIncome: 0m,
                    OperatingLines: [],
                    InvestingLines: [],
                    FinancingLines: [],
                    BeginningCash: 0m,
                    EndingCash: 0m,
                    BeginningLatestClosedPeriod: null,
                    BeginningRollForwardPeriods: 0,
                    EndingLatestClosedPeriod: null,
                    EndingRollForwardPeriods: 0,
                    UnclassifiedCashRows:
                    [
                        new CashFlowIndirectUnclassifiedCashRow("1500", "Equipment", -25m)
                    ])),
            new SpyLogger<CashFlowIndirectReportService>());

        var act = () => service.GetAsync(
            new CashFlowIndirectReportRequest
            {
                FromInclusive = new DateOnly(2026, 3, 1),
                ToInclusive = new DateOnly(2026, 3, 1)
            },
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<AccountingReportValidationException>();
        ex.Which.ErrorCode.Should().Be("accounting.validation.cash_flow_indirect.unclassified_cash");
        ex.Which.Message.Should().Match("*unclassified balance-sheet counterparties*1500 Equipment*");
    }

    [Fact]
    public async Task GetAsync_WhenReconciliationDoesNotMatch_ThrowsValidationException()
    {
        var service = new CashFlowIndirectReportService(
            new StubSnapshotReader(
                new CashFlowIndirectSnapshot(
                    NetIncome: 20m,
                    OperatingLines: [],
                    InvestingLines: [],
                    FinancingLines: [],
                    BeginningCash: 0m,
                    EndingCash: 5m,
                    BeginningLatestClosedPeriod: null,
                    BeginningRollForwardPeriods: 0,
                    EndingLatestClosedPeriod: null,
                    EndingRollForwardPeriods: 0,
                    UnclassifiedCashRows: [])),
            new SpyLogger<CashFlowIndirectReportService>());

        var act = () => service.GetAsync(
            new CashFlowIndirectReportRequest
            {
                FromInclusive = new DateOnly(2026, 3, 1),
                ToInclusive = new DateOnly(2026, 3, 1)
            },
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<AccountingReportValidationException>();
        ex.Which.ErrorCode.Should().Be("accounting.validation.cash_flow_indirect.reconciliation_failed");
        ex.Which.Message.Should().Match("*failed reconciliation*");
    }

    [Fact]
    public async Task GetAsync_WhenBeginningAndEndingUseInceptionToDate_Fallback_LogsWarnings()
    {
        var logger = new SpyLogger<CashFlowIndirectReportService>();
        var service = new CashFlowIndirectReportService(
            new StubSnapshotReader(
                new CashFlowIndirectSnapshot(
                    NetIncome: 0m,
                    OperatingLines: [],
                    InvestingLines: [],
                    FinancingLines: [],
                    BeginningCash: 0m,
                    EndingCash: 0m,
                    BeginningLatestClosedPeriod: null,
                    BeginningRollForwardPeriods: 0,
                    EndingLatestClosedPeriod: null,
                    EndingRollForwardPeriods: 0,
                    UnclassifiedCashRows: [])),
            logger);

        await service.GetAsync(
            new CashFlowIndirectReportRequest
            {
                FromInclusive = new DateOnly(2026, 3, 1),
                ToInclusive = new DateOnly(2026, 3, 1)
            },
            CancellationToken.None);

        logger.Entries.Should().Contain(x => x.Level == LogLevel.Warning && x.Message.Contains("beginning cash endpoint is using inception-to-date register activity", StringComparison.Ordinal));
        logger.Entries.Should().Contain(x => x.Level == LogLevel.Warning && x.Message.Contains("ending cash endpoint is using inception-to-date register activity", StringComparison.Ordinal));
    }

    private sealed class StubSnapshotReader(CashFlowIndirectSnapshot snapshot) : ICashFlowIndirectSnapshotReader
    {
        public Task<CashFlowIndirectSnapshot> GetAsync(
            DateOnly fromInclusive,
            DateOnly toInclusive,
            CancellationToken ct = default)
            => Task.FromResult(snapshot);
    }

    private sealed class SpyLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
    }

    private sealed record LogEntry(LogLevel Level, string Message);
}
