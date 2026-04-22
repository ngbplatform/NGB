using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NGB.Accounting.Accounts;
using NGB.Accounting.Reports.StatementOfChangesInEquity;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Reporting;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class StatementOfChangesInEquityReportService_P0Tests
{
    [Fact]
    public async Task GetAsync_Builds_Equity_Rollforward_With_Synthetic_CurrentEarnings_Component()
    {
        var service = new StatementOfChangesInEquityReportService(
            new StubSnapshotReader(
                new StatementOfChangesInEquitySnapshot(
                [
                    new StatementOfChangesInEquitySnapshotRow(Guid.Parse("11111111-1111-1111-1111-111111111111"), "80", "Owner's Equity", StatementSection.Equity, -100m, -250m),
                    new StatementOfChangesInEquitySnapshotRow(Guid.Parse("22222222-2222-2222-2222-222222222222"), "84", "Retained Earnings", StatementSection.Equity, -50m, -75m),
                    new StatementOfChangesInEquitySnapshotRow(Guid.Parse("33333333-3333-3333-3333-333333333333"), "4000", "Rental Income", StatementSection.Income, -10m, -60m),
                    new StatementOfChangesInEquitySnapshotRow(Guid.Parse("44444444-4444-4444-4444-444444444444"), "6000", "Operating Expense", StatementSection.Expenses, 4m, 15m)
                ],
                new DateOnly(2026, 1, 1),
                1,
                new DateOnly(2026, 3, 1),
                0)),
            NullLogger<StatementOfChangesInEquityReportService>.Instance);

        var report = await service.GetAsync(
            new StatementOfChangesInEquityReportRequest
            {
                FromInclusive = new DateOnly(2026, 2, 1),
                ToInclusive = new DateOnly(2026, 3, 1)
            },
            CancellationToken.None);

        report.Lines.Select(x => x.ComponentCode).Should().Equal("80", "84", "CURR_EARNINGS");

        report.Lines[0].OpeningAmount.Should().Be(100m);
        report.Lines[0].ChangeAmount.Should().Be(150m);
        report.Lines[0].ClosingAmount.Should().Be(250m);

        report.Lines[1].OpeningAmount.Should().Be(50m);
        report.Lines[1].ChangeAmount.Should().Be(25m);
        report.Lines[1].ClosingAmount.Should().Be(75m);

        report.Lines[2].IsSynthetic.Should().BeTrue();
        report.Lines[2].OpeningAmount.Should().Be(6m);
        report.Lines[2].ChangeAmount.Should().Be(39m);
        report.Lines[2].ClosingAmount.Should().Be(45m);

        report.TotalOpening.Should().Be(156m);
        report.TotalChange.Should().Be(214m);
        report.TotalClosing.Should().Be(370m);
    }

    [Fact]
    public async Task GetAsync_WhenSnapshotContainsUnexpectedSection_ThrowsInvariant()
    {
        var service = new StatementOfChangesInEquityReportService(
            new StubSnapshotReader(
                new StatementOfChangesInEquitySnapshot(
                [
                    new StatementOfChangesInEquitySnapshotRow(Guid.NewGuid(), "1000", "Cash", StatementSection.Assets, 1m, 2m)
                ],
                null,
                0,
                null,
                0)),
            NullLogger<StatementOfChangesInEquityReportService>.Instance);

        var act = () => service.GetAsync(
            new StatementOfChangesInEquityReportRequest
            {
                FromInclusive = new DateOnly(2026, 3, 1),
                ToInclusive = new DateOnly(2026, 3, 1)
            },
            CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>();
    }

    [Fact]
    public async Task GetAsync_WhenOpeningAndClosingNeedInceptionToDate_Fallback_LogsWarnings()
    {
        var logger = new SpyLogger<StatementOfChangesInEquityReportService>();
        var service = new StatementOfChangesInEquityReportService(
            new StubSnapshotReader(
                new StatementOfChangesInEquitySnapshot(
                    [],
                    null,
                    0,
                    null,
                    0)),
            logger);

        await service.GetAsync(
            new StatementOfChangesInEquityReportRequest
            {
                FromInclusive = new DateOnly(2026, 3, 1),
                ToInclusive = new DateOnly(2026, 3, 1)
            },
            CancellationToken.None);

        logger.Entries.Should().Contain(x => x.Level == LogLevel.Warning && x.Message.Contains("opening endpoint is using inception-to-date turnovers", StringComparison.Ordinal));
        logger.Entries.Should().Contain(x => x.Level == LogLevel.Warning && x.Message.Contains("closing endpoint is using inception-to-date turnovers", StringComparison.Ordinal));
    }

    private sealed class StubSnapshotReader(StatementOfChangesInEquitySnapshot snapshot) : IStatementOfChangesInEquitySnapshotReader
    {
        public Task<StatementOfChangesInEquitySnapshot> GetAsync(
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
