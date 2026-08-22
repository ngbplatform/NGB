using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NGB.Accounting.Accounts;
using NGB.Accounting.Reports.BalanceSheet;
using NGB.Core.Dimensions;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Reporting;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class BalanceSheetReportService_P0Tests
{
    [Fact]
    public async Task GetAsync_WhenRequestIsNull_ThrowsArgumentRequired()
    {
        var service = new BalanceSheetReportService(null!, null!, null!, NullLogger<BalanceSheetReportService>.Instance);

        var action = () => service.GetAsync(null!, default);

        await action.Should().ThrowAsync<NgbArgumentRequiredException>();
    }

    [Fact]
    public async Task GetAsync_WhenPeriodIsNotMonthStart_ThrowsOutOfRange()
    {
        var service = new BalanceSheetReportService(null!, null!, null!, NullLogger<BalanceSheetReportService>.Instance);

        var action = () => service.GetAsync(new BalanceSheetReportRequest
        {
            AsOfPeriod = new DateOnly(2026, 3, 2)
        });

        await action.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task GetAsync_ResolvesInactiveAccountsAggregatesRowsSortsAndAddsNetIncome()
    {
        var assetAId = Guid.CreateVersion7();
        var assetBId = Guid.CreateVersion7();
        var assetCId = Guid.CreateVersion7();
        var liabilityId = Guid.CreateVersion7();
        var equityId = Guid.CreateVersion7();
        var incomeId = Guid.CreateVersion7();
        var otherIncomeId = Guid.CreateVersion7();
        var cogsId = Guid.CreateVersion7();
        var expenseId = Guid.CreateVersion7();
        var otherExpenseId = Guid.CreateVersion7();

        var activeAccounts = new[]
        {
            new Account(assetAId, "1000", "Zulu", AccountType.Asset),
            new Account(assetCId, "1100", "Zero", AccountType.Asset),
            new Account(equityId, "3000", "Equity", AccountType.Equity),
            new Account(incomeId, "4000", "Income", AccountType.Income),
            new Account(otherIncomeId, "4100", "Other income", AccountType.Income, StatementSection.OtherIncome),
            new Account(cogsId, "5000", "COGS", AccountType.Expense, StatementSection.CostOfGoodsSold),
            new Account(expenseId, "6000", "Expense", AccountType.Expense),
            new Account(otherExpenseId, "7000", "Other expense", AccountType.Expense, StatementSection.OtherExpense)
        };
        var inactiveAsset = new Account(assetBId, "1000", "Alpha", AccountType.Asset);
        var inactiveLiability = new Account(liabilityId, "2000", "Inactive liability", AccountType.Liability);
        var service = new BalanceSheetReportService(
            new StubBalanceSheetSnapshotReader(new BalanceSheetSnapshot(
            [
                new BalanceSheetSnapshotRow(Guid.Empty, 999m),
                new BalanceSheetSnapshotRow(assetAId, 10m),
                new BalanceSheetSnapshotRow(assetAId, 5m),
                new BalanceSheetSnapshotRow(assetBId, 2m),
                new BalanceSheetSnapshotRow(assetCId, 0m),
                new BalanceSheetSnapshotRow(liabilityId, -17m),
                new BalanceSheetSnapshotRow(liabilityId, 0m),
                new BalanceSheetSnapshotRow(equityId, -17m),
                new BalanceSheetSnapshotRow(incomeId, -20m),
                new BalanceSheetSnapshotRow(otherIncomeId, -3m),
                new BalanceSheetSnapshotRow(cogsId, 2m),
                new BalanceSheetSnapshotRow(expenseId, 5m),
                new BalanceSheetSnapshotRow(otherExpenseId, 1m)
            ], new DateOnly(2026, 3, 1), 0)),
            new StubChartOfAccountsProvider(activeAccounts),
            new StubAccountByIdResolver(inactiveAsset, inactiveLiability),
            NullLogger<BalanceSheetReportService>.Instance);

        var report = await service.GetAsync(new BalanceSheetReportRequest
        {
            AsOfPeriod = new DateOnly(2026, 3, 1),
            IncludeZeroAccounts = true,
            IncludeNetIncomeInEquity = true
        });

        report.Sections.Single(section => section.Section == StatementSection.Assets)
            .Lines.Select(line => (line.AccountCode, line.AccountName, line.Amount)).Should().Equal(
                ("1000", "Alpha", 2m),
                ("1000", "Zulu", 15m),
                ("1100", "Zero", 0m));
        report.Sections.Single(section => section.Section == StatementSection.Liabilities)
            .Lines.Should().ContainSingle(line => line.AccountId == liabilityId && line.Amount == 17m);
        report.Sections.Single(section => section.Section == StatementSection.Equity)
            .Lines.Should().Contain(line => line.AccountCode == "NET" && line.Amount == 15m);
    }

    [Fact]
    public async Task GetAsync_WhenZeroAccountExcluded_SkipsItAndCanSkipNetIncomeCalculation()
    {
        var accountId = Guid.CreateVersion7();
        var service = new BalanceSheetReportService(
            new StubBalanceSheetSnapshotReader(new BalanceSheetSnapshot(
                [new BalanceSheetSnapshotRow(accountId, 0m)],
                new DateOnly(2026, 3, 1),
                0)),
            new StubChartOfAccountsProvider([new Account(accountId, "1000", "Zero", AccountType.Asset)]),
            new StubAccountByIdResolver(),
            NullLogger<BalanceSheetReportService>.Instance);

        var report = await service.GetAsync(new BalanceSheetReportRequest
        {
            AsOfPeriod = new DateOnly(2026, 3, 1),
            IncludeZeroAccounts = false,
            IncludeNetIncomeInEquity = false
        });

        report.Sections.SelectMany(section => section.Lines).Should().BeEmpty();
    }

    [Fact]
    public async Task GetAsync_WhenHistoricalAccountCannotBeResolved_ThrowsNotFound()
    {
        var accountId = Guid.CreateVersion7();
        var service = new BalanceSheetReportService(
            new StubBalanceSheetSnapshotReader(new BalanceSheetSnapshot(
                [new BalanceSheetSnapshotRow(accountId, 1m)],
                new DateOnly(2026, 3, 1),
                0)),
            new StubChartOfAccountsProvider(),
            new StubAccountByIdResolver(),
            NullLogger<BalanceSheetReportService>.Instance);

        var action = () => service.GetAsync(new BalanceSheetReportRequest
        {
            AsOfPeriod = new DateOnly(2026, 3, 1)
        });

        await action.Should().ThrowAsync<AccountNotFoundException>();
    }

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

    private sealed class StubAccountByIdResolver(params Account[] accounts) : IAccountByIdResolver
    {
        private readonly IReadOnlyDictionary<Guid, Account> _accounts = accounts.ToDictionary(account => account.Id);

        public Task<Account?> GetByIdAsync(Guid accountId, CancellationToken ct = default)
            => Task.FromResult(_accounts.GetValueOrDefault(accountId));

        public Task<IReadOnlyDictionary<Guid, Account>> GetByIdsAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<Guid, Account>>(
                accountIds.Where(_accounts.ContainsKey).ToDictionary(id => id, id => _accounts[id]));
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
