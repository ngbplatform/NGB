using FluentAssertions;
using NGB.Accounting.Accounts;
using NGB.Accounting.Balances;
using NGB.Accounting.Reports.AccountCard;
using NGB.Accounting.Turnovers;
using NGB.Core.Dimensions;
using NGB.Persistence.Accounts;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Reporting;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class AccountCardEffectivePagedReportService_P0Tests
{
    [Fact]
    public async Task GetPageAsync_WhenIntermediatePage_LoadsGrandTotalsOnce_AndCarriesThemInCursor()
    {
        var accountId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var reader = new StubEffectivePageReader(
            page: new AccountCardLinePage
            {
                Lines =
                [
                    new AccountCardLine
                    {
                        EntryId = 10,
                        PeriodUtc = new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc),
                        DocumentId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                        AccountId = accountId,
                        AccountCode = "1000",
                        CounterAccountId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                        CounterAccountCode = "4900",
                        CounterAccountDimensionSetId = Guid.Empty,
                        DimensionSetId = Guid.Empty,
                        DebitAmount = 25m,
                        CreditAmount = 0m
                    }
                ],
                HasMore = true,
                NextCursor = new AccountCardLineCursor
                {
                    AfterPeriodUtc = new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc),
                    AfterEntryId = 10
                },
                TotalDebit = 99m,
                TotalCredit = 11m
            });

        var balanceReader = new StubBalanceReader([]);
        var turnoverReader = new StubTurnoverReader([]);
        var service = new AccountCardEffectivePagedReportService(
            reader,
            balanceReader,
            turnoverReader,
            new StubChartOfAccountsRepository(accountId, "1000"));

        var page = await service.GetPageAsync(new AccountCardReportPageRequest
        {
            AccountId = accountId,
            FromInclusive = new DateOnly(2026, 3, 1),
            ToInclusive = new DateOnly(2026, 3, 1),
            PageSize = 1
        }, CancellationToken.None);

        reader.TotalsCallCount.Should().Be(1);
        balanceReader.LatestClosedCallCount.Should().Be(1);
        turnoverReader.RangeCallCount.Should().Be(1);
        reader.PageRequests.Should().ContainSingle();
        reader.PageRequests[0].IncludeTotals.Should().BeTrue();
        page.OpeningBalance.Should().Be(0m);
        page.TotalDebit.Should().Be(99m);
        page.TotalCredit.Should().Be(11m);
        page.ClosingBalance.Should().Be(88m);
        page.HasMore.Should().BeTrue();
        page.NextCursor.Should().NotBeNull();
        page.NextCursor!.RunningBalance.Should().Be(25m);
        page.NextCursor.TotalDebit.Should().Be(99m);
        page.NextCursor.TotalCredit.Should().Be(11m);
        page.NextCursor.ClosingBalance.Should().Be(88m);
        page.Lines.Should().ContainSingle();
        page.Lines[0].RunningBalance.Should().Be(25m);
    }

    [Fact]
    public async Task GetPageAsync_WhenCursorAlreadyCarriesGrandTotals_DoesNotReloadTotals()
    {
        var accountId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var reader = new StubEffectivePageReader(
            page: new AccountCardLinePage
            {
                Lines =
                [
                    new AccountCardLine
                    {
                        EntryId = 20,
                        PeriodUtc = new DateTime(2026, 3, 11, 12, 0, 0, DateTimeKind.Utc),
                        DocumentId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                        AccountId = accountId,
                        AccountCode = "1000",
                        CounterAccountId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
                        CounterAccountCode = "4900",
                        CounterAccountDimensionSetId = Guid.Empty,
                        DimensionSetId = Guid.Empty,
                        DebitAmount = 10m,
                        CreditAmount = 0m
                    }
                ],
                HasMore = false,
                NextCursor = null
            });

        var balanceReader = new StubBalanceReader([]);
        var turnoverReader = new StubTurnoverReader([]);
        var service = new AccountCardEffectivePagedReportService(
            reader,
            balanceReader,
            turnoverReader,
            new StubChartOfAccountsRepository(accountId, "1000"));

        var page = await service.GetPageAsync(new AccountCardReportPageRequest
        {
            AccountId = accountId,
            FromInclusive = new DateOnly(2026, 3, 1),
            ToInclusive = new DateOnly(2026, 3, 1),
            PageSize = 1,
            Cursor = new AccountCardReportCursor
            {
                AfterPeriodUtc = new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc),
                AfterEntryId = 10,
                RunningBalance = 20m,
                TotalDebit = 35m,
                TotalCredit = 5m,
                ClosingBalance = 30m
            }
        }, CancellationToken.None);

        reader.TotalsCallCount.Should().Be(0);
        balanceReader.LatestClosedCallCount.Should().Be(0);
        turnoverReader.RangeCallCount.Should().Be(0);
        reader.PageRequests.Should().ContainSingle();
        reader.PageRequests[0].IncludeTotals.Should().BeFalse();
        page.OpeningBalance.Should().Be(20m);
        page.TotalDebit.Should().Be(35m);
        page.TotalCredit.Should().Be(5m);
        page.ClosingBalance.Should().Be(30m);
        page.HasMore.Should().BeFalse();
        page.NextCursor.Should().BeNull();
        page.Lines.Should().ContainSingle();
        page.Lines[0].RunningBalance.Should().Be(30m);
    }

    [Fact]
    public async Task GetPageAsync_WhenClosedSnapshotIsMissing_ReconstructsOpeningFromHistoricalTurnovers()
    {
        var accountId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var reader = new StubEffectivePageReader(
            page: new AccountCardLinePage
            {
                Lines =
                [
                    new AccountCardLine
                    {
                        EntryId = 30,
                        PeriodUtc = new DateTime(2026, 3, 12, 12, 0, 0, DateTimeKind.Utc),
                        DocumentId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
                        AccountId = accountId,
                        AccountCode = "1000",
                        CounterAccountId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
                        CounterAccountCode = "4900",
                        CounterAccountDimensionSetId = Guid.Empty,
                        DimensionSetId = Guid.Empty,
                        DebitAmount = 10m,
                        CreditAmount = 0m
                    }
                ],
                HasMore = false,
                NextCursor = null,
                TotalDebit = 10m,
                TotalCredit = 0m
            });

        var service = new AccountCardEffectivePagedReportService(
            reader,
            new StubBalanceReader([]),
            new StubTurnoverReader([
                new AccountingTurnover
                {
                    Period = new DateOnly(2026, 1, 1),
                    AccountId = accountId,
                    DimensionSetId = Guid.Empty,
                    AccountCode = "1000",
                    DebitAmount = 100m,
                    CreditAmount = 0m,
                    Dimensions = DimensionBag.Empty
                },
                new AccountingTurnover
                {
                    Period = new DateOnly(2026, 2, 1),
                    AccountId = accountId,
                    DimensionSetId = Guid.Empty,
                    AccountCode = "1000",
                    DebitAmount = 0m,
                    CreditAmount = 40m,
                    Dimensions = DimensionBag.Empty
                }
            ]),
            new StubChartOfAccountsRepository(accountId, "1000"));

        var page = await service.GetPageAsync(new AccountCardReportPageRequest
        {
            AccountId = accountId,
            FromInclusive = new DateOnly(2026, 3, 1),
            ToInclusive = new DateOnly(2026, 3, 1),
            PageSize = 50
        }, CancellationToken.None);

        page.OpeningBalance.Should().Be(60m);
        page.TotalDebit.Should().Be(10m);
        page.TotalCredit.Should().Be(0m);
        page.ClosingBalance.Should().Be(70m);
        page.Lines.Should().ContainSingle();
        page.Lines[0].RunningBalance.Should().Be(70m);
    }

    [Fact]
    public async Task GetPageAsync_WhenPagingIsDisabled_Propagates_Unpaged_Mode_To_Effective_Reader()
    {
        var accountId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var reader = new StubEffectivePageReader(
            page: new AccountCardLinePage
            {
                Lines =
                [
                    new AccountCardLine
                    {
                        EntryId = 10,
                        PeriodUtc = new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc),
                        DocumentId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                        AccountId = accountId,
                        AccountCode = "1000",
                        CounterAccountId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                        CounterAccountCode = "4900",
                        CounterAccountDimensionSetId = Guid.Empty,
                        DimensionSetId = Guid.Empty,
                        DebitAmount = 25m,
                        CreditAmount = 0m
                    },
                    new AccountCardLine
                    {
                        EntryId = 20,
                        PeriodUtc = new DateTime(2026, 3, 11, 12, 0, 0, DateTimeKind.Utc),
                        DocumentId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                        AccountId = accountId,
                        AccountCode = "1000",
                        CounterAccountId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
                        CounterAccountCode = "4900",
                        CounterAccountDimensionSetId = Guid.Empty,
                        DimensionSetId = Guid.Empty,
                        DebitAmount = 10m,
                        CreditAmount = 0m
                    }
                ],
                HasMore = false,
                NextCursor = null,
                TotalDebit = 35m,
                TotalCredit = 5m
            });

        var balanceReader = new StubBalanceReader([]);
        var turnoverReader = new StubTurnoverReader([]);
        var service = new AccountCardEffectivePagedReportService(
            reader,
            balanceReader,
            turnoverReader,
            new StubChartOfAccountsRepository(accountId, "1000"));

        var page = await service.GetPageAsync(new AccountCardReportPageRequest
        {
            AccountId = accountId,
            FromInclusive = new DateOnly(2026, 3, 1),
            ToInclusive = new DateOnly(2026, 3, 1),
            PageSize = 1,
            Cursor = new AccountCardReportCursor
            {
                AfterPeriodUtc = new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc),
                AfterEntryId = 10,
                RunningBalance = 20m,
                TotalDebit = 35m,
                TotalCredit = 5m,
                ClosingBalance = 30m
            },
            DisablePaging = true
        }, CancellationToken.None);

        reader.PageRequests.Should().ContainSingle();
        reader.PageRequests[0].DisablePaging.Should().BeTrue();
        reader.PageRequests[0].Cursor.Should().BeNull();
        reader.PageRequests[0].IncludeTotals.Should().BeTrue();
        page.HasMore.Should().BeFalse();
        page.Lines.Should().HaveCount(2);
    }

    private sealed class StubEffectivePageReader(AccountCardLinePage page) : IAccountCardEffectivePageReader
    {
        public List<AccountCardLinePageRequest> PageRequests { get; } = [];
        public int TotalsCallCount => PageRequests.Count(x => x.IncludeTotals);

        public Task<AccountCardLinePage> GetPageAsync(AccountCardLinePageRequest request, CancellationToken ct = default)
        {
            PageRequests.Add(request);
            return Task.FromResult(page);
        }
    }

    private sealed class StubBalanceReader(IReadOnlyList<AccountingBalance> rows) : IAccountingBalanceReader
    {
        public int ForPeriodCallCount { get; private set; }
        public int LatestClosedCallCount { get; private set; }

        public Task<IReadOnlyList<AccountingBalance>> GetForPeriodAsync(DateOnly period, CancellationToken ct = default) => Task.FromResult(rows);
        public Task<IReadOnlyList<AccountingBalance>> GetLatestClosedAsync(DateOnly period, CancellationToken ct = default)
        {
            LatestClosedCallCount++;
            return Task.FromResult(rows);
        }
    }

    private sealed class StubTurnoverReader(IReadOnlyList<AccountingTurnover> rows) : IAccountingTurnoverReader
    {
        public int ForPeriodCallCount { get; private set; }
        public int RangeCallCount { get; private set; }

        public Task<IReadOnlyList<AccountingTurnover>> GetForPeriodAsync(DateOnly period, CancellationToken ct = default) => Task.FromResult(rows);
        public Task<IReadOnlyList<AccountingTurnover>> GetRangeAsync(DateOnly fromInclusive, DateOnly toInclusive, CancellationToken ct = default)
        {
            RangeCallCount++;
            return Task.FromResult(rows);
        }
    }

    private sealed class StubChartOfAccountsRepository(Guid stubAccountId, string stubCode) : IChartOfAccountsRepository
    {
        public Task<IReadOnlyList<Account>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Account>>([]);

        public Task<IReadOnlyList<ChartOfAccountsAdminItem>> GetForAdminAsync(bool includeDeleted = false, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ChartOfAccountsAdminItem>>([]);

        public Task<ChartOfAccountsAdminItem?> GetAdminByIdAsync(Guid accountId, CancellationToken ct = default)
            => Task.FromResult<ChartOfAccountsAdminItem?>(null);

        public Task<IReadOnlyList<ChartOfAccountsAdminItem>> GetAdminByIdsAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ChartOfAccountsAdminItem>>([]);

        public Task<bool> HasMovementsAsync(Guid accountId, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task CreateAsync(Account account, bool isActive = true, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<string?> GetCodeByIdAsync(Guid accountId, CancellationToken ct = default)
            => Task.FromResult<string?>(accountId == stubAccountId ? stubCode : null);

        public Task UpdateAsync(Account account, bool isActive, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SetActiveAsync(Guid accountId, bool isActive, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task MarkForDeletionAsync(Guid accountId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task UnmarkForDeletionAsync(Guid accountId, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
