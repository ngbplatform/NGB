using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NGB.Accounting.Accounts;
using NGB.Accounting.Reports.BalanceSheet;
using NGB.Core.Dimensions;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Reporting;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class BalanceSheetReportService_P0Tests
{
    [Fact]
    public async Task GetAsync_Forwards_AsOfPeriod_And_DimensionScopes_To_SnapshotReader()
    {
        var snapshotReader = new StubBalanceSheetSnapshotReader(new BalanceSheetSnapshot([], new DateOnly(2026, 1, 1), 2));
        var service = new BalanceSheetReportService(
            snapshotReader,
            new StubChartOfAccountsProvider(),
            new StubAccountByIdResolver(),
            NullLogger<BalanceSheetReportService>.Instance);
        var propertyDimensionId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var propertyValueId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var scopes = new DimensionScopeBag([new DimensionScope(propertyDimensionId, [propertyValueId], includeDescendants: true)]);

        var result = await service.GetAsync(
            new BalanceSheetReportRequest
            {
                AsOfPeriod = new DateOnly(2026, 3, 1),
                DimensionScopes = scopes,
                IncludeZeroAccounts = false,
                IncludeNetIncomeInEquity = true
            },
            CancellationToken.None);

        snapshotReader.RequestedAsOfPeriod.Should().Be(new DateOnly(2026, 3, 1));
        snapshotReader.RequestedScopes.Should().BeSameAs(scopes);
        result.AsOfPeriod.Should().Be(new DateOnly(2026, 3, 1));
        result.IsBalanced.Should().BeTrue();
        result.Difference.Should().Be(0m);
    }

    [Fact]
    public async Task GetAsync_WhenSnapshotReaderReturnsAsOfSnapshot_DoesNotLogWarnings()
    {
        var accountId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var logger = new SpyLogger<BalanceSheetReportService>();
        var service = new BalanceSheetReportService(
            new StubBalanceSheetSnapshotReader(
                new BalanceSheetSnapshot(
                [
                    new BalanceSheetSnapshotRow(accountId, 100m)
                ],
                new DateOnly(2026, 3, 1),
                0)),
            new StubChartOfAccountsProvider(
            [
                new Account(accountId, "1000", "Operating Cash", AccountType.Asset)
            ]),
            new StubAccountByIdResolver(),
            logger);

        var result = await service.GetAsync(
            new BalanceSheetReportRequest
            {
                AsOfPeriod = new DateOnly(2026, 3, 1),
                IncludeZeroAccounts = false,
                IncludeNetIncomeInEquity = true
            },
            CancellationToken.None);

        logger.Entries.Should().BeEmpty();
        result.AsOfPeriod.Should().Be(new DateOnly(2026, 3, 1));
        result.Sections
            .Single(x => x.Section == StatementSection.Assets)
            .Lines.Should()
            .ContainSingle(x => x.AccountCode == "1000" && x.Amount == 100m);
    }

    [Fact]
    public async Task GetAsync_WhenSnapshotReaderHasNoClosedPeriod_LogsWarning()
    {
        var logger = new SpyLogger<BalanceSheetReportService>();
        var service = new BalanceSheetReportService(
            new StubBalanceSheetSnapshotReader(new BalanceSheetSnapshot([], null, 0)),
            new StubChartOfAccountsProvider(),
            new StubAccountByIdResolver(),
            logger);

        await service.GetAsync(
            new BalanceSheetReportRequest
            {
                AsOfPeriod = new DateOnly(2026, 3, 1),
                IncludeZeroAccounts = false,
                IncludeNetIncomeInEquity = true
            },
            CancellationToken.None);

        logger.Entries.Should().ContainSingle(x => x.Level == LogLevel.Warning && x.Message.Contains("inception-to-date turnovers", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetAsync_WhenRollForwardSpansManyPeriods_LogsWarning()
    {
        var logger = new SpyLogger<BalanceSheetReportService>();
        var service = new BalanceSheetReportService(
            new StubBalanceSheetSnapshotReader(new BalanceSheetSnapshot([], new DateOnly(2025, 1, 1), 15)),
            new StubChartOfAccountsProvider(),
            new StubAccountByIdResolver(),
            logger);

        await service.GetAsync(
            new BalanceSheetReportRequest
            {
                AsOfPeriod = new DateOnly(2026, 3, 1),
                IncludeZeroAccounts = false,
                IncludeNetIncomeInEquity = true
            },
            CancellationToken.None);

        logger.Entries.Should().ContainSingle(x => x.Level == LogLevel.Warning && x.Message.Contains("roll-forward is spanning many periods", StringComparison.Ordinal));
    }

    private sealed class StubBalanceSheetSnapshotReader(BalanceSheetSnapshot snapshot) : IBalanceSheetSnapshotReader
    {
        public DateOnly RequestedAsOfPeriod { get; private set; }
        public DimensionScopeBag? RequestedScopes { get; private set; }
        public int CallCount { get; private set; }

        public Task<BalanceSheetSnapshot> GetAsync(
            DateOnly asOfPeriod,
            DimensionScopeBag? dimensionScopes,
            CancellationToken ct = default)
        {
            CallCount++;
            RequestedAsOfPeriod = asOfPeriod;
            RequestedScopes = dimensionScopes;
            return Task.FromResult(snapshot);
        }
    }

    private sealed class StubChartOfAccountsProvider(IReadOnlyList<Account>? accounts = null) : IChartOfAccountsProvider
    {
        public Task<ChartOfAccounts> GetAsync(CancellationToken ct = default)
        {
            var chart = new ChartOfAccounts();

            if (accounts is not null)
            {
                foreach (var account in accounts)
                    chart.Add(account);
            }

            return Task.FromResult(chart);
        }
    }

    private sealed class StubAccountByIdResolver : IAccountByIdResolver
    {
        public Task<Account?> GetByIdAsync(Guid accountId, CancellationToken ct = default)
            => Task.FromResult<Account?>(null);

        public Task<IReadOnlyDictionary<Guid, Account>> GetByIdsAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<Guid, Account>>(new Dictionary<Guid, Account>());
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
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message);
}
