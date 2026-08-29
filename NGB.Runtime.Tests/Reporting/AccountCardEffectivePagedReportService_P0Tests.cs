using FluentAssertions;
using NGB.Accounting.Accounts;
using NGB.Accounting.Reports.AccountCard;
using NGB.Core.Dimensions;
using NGB.Persistence.Accounts;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Reporting;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class AccountCardEffectivePagedReportService_P0Tests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetPageAsync_WhenRequestedTotalsAreMissing_ThrowsInvariantViolation(bool missingDebit)
    {
        var accountId = Guid.CreateVersion7();
        var service = new AccountCardEffectivePagedReportService(
            new StubEffectivePageReader(new AccountCardLinePage
            {
                Lines = [],
                TotalDebit = missingDebit ? null : 0m,
                TotalCredit = missingDebit ? 0m : null
            }),
            new StubChartOfAccountsRepository(accountId, null));

        var action = () => service.GetPageAsync(new AccountCardReportPageRequest
        {
            AccountId = accountId,
            FromInclusive = new DateOnly(2026, 1, 1),
            ToInclusive = new DateOnly(2026, 1, 1)
        });

        await action.Should().ThrowAsync<NgbInvariantViolationException>()
            .WithMessage(missingDebit ? "*total debit*" : "*total credit*");
    }

    [Theory]
    [InlineData("1000")]
    [InlineData(null)]
    public async Task GetPageAsync_WhenPageIsEmpty_UsesRepositoryCodeOrAccountIdFallback(string? repositoryCode)
    {
        var accountId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var service = new AccountCardEffectivePagedReportService(
            new StubEffectivePageReader(new AccountCardLinePage
            {
                Lines = [],
                HasMore = true,
                TotalDebit = 0m,
                TotalCredit = 0m
            }),
            new StubChartOfAccountsRepository(accountId, repositoryCode));

        var page = await service.GetPageAsync(new AccountCardReportPageRequest
        {
            AccountId = accountId,
            FromInclusive = new DateOnly(2026, 1, 1),
            ToInclusive = new DateOnly(2026, 1, 1)
        });

        page.AccountCode.Should().Be(repositoryCode ?? accountId.ToString());
        page.Lines.Should().BeEmpty();
        page.HasMore.Should().BeTrue();
        page.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task GetPageAsync_WhenRequestIsNull_ThrowsArgumentRequired()
    {
        var service = new AccountCardEffectivePagedReportService(null!, null!);

        var action = () => service.GetPageAsync(null!, default);

        await action.Should().ThrowAsync<NgbArgumentRequiredException>();
    }

    [Fact]
    public async Task GetPageAsync_WhenAccountIdIsEmpty_ThrowsArgumentRequired()
    {
        var service = new AccountCardEffectivePagedReportService(null!, null!);

        var action = () => service.GetPageAsync(new AccountCardReportPageRequest
        {
            AccountId = Guid.Empty,
            FromInclusive = new DateOnly(2026, 1, 1),
            ToInclusive = new DateOnly(2026, 1, 1)
        });

        await action.Should().ThrowAsync<NgbArgumentRequiredException>();
    }

    [Fact]
    public async Task GetPageAsync_WhenRangeIsReversed_ThrowsOutOfRange()
    {
        var service = new AccountCardEffectivePagedReportService(null!, null!);

        var action = () => service.GetPageAsync(new AccountCardReportPageRequest
        {
            AccountId = Guid.CreateVersion7(),
            FromInclusive = new DateOnly(2026, 2, 1),
            ToInclusive = new DateOnly(2026, 1, 1)
        });

        await action.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetPageAsync_WhenRangeBoundaryIsNotMonthStart_ThrowsOutOfRange(bool invalidFrom)
    {
        var service = new AccountCardEffectivePagedReportService(null!, null!);
        var request = new AccountCardReportPageRequest
        {
            AccountId = Guid.CreateVersion7(),
            FromInclusive = new DateOnly(2026, 1, invalidFrom ? 2 : 1),
            ToInclusive = new DateOnly(2026, 2, invalidFrom ? 1 : 2)
        };

        var action = () => service.GetPageAsync(request);

        await action.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
    }

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

        var service = new AccountCardEffectivePagedReportService(
            reader,
            new StubChartOfAccountsRepository(accountId, "1000"));

        var page = await service.GetPageAsync(new AccountCardReportPageRequest
        {
            AccountId = accountId,
            FromInclusive = new DateOnly(2026, 3, 1),
            ToInclusive = new DateOnly(2026, 3, 1),
            PageSize = 1
        }, CancellationToken.None);

        reader.TotalsCallCount.Should().Be(1);
        reader.OpeningBalanceCallCount.Should().Be(1);
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

        var service = new AccountCardEffectivePagedReportService(
            reader,
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
        reader.OpeningBalanceCallCount.Should().Be(0);
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
            },
            openingBalance: 60m);

        var service = new AccountCardEffectivePagedReportService(
            reader,
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

        var service = new AccountCardEffectivePagedReportService(
            reader,
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

    private sealed class StubEffectivePageReader(AccountCardLinePage page, decimal openingBalance = 0m) : IAccountCardEffectivePageReader
    {
        public List<AccountCardLinePageRequest> PageRequests { get; } = [];
        public int TotalsCallCount => PageRequests.Count(x => x.IncludeTotals);
        public int OpeningBalanceCallCount { get; private set; }

        public Task<decimal> GetOpeningBalanceAsync(
            Guid accountId,
            DateOnly fromInclusive,
            DimensionScopeBag? dimensionScopes,
            CancellationToken ct = default)
        {
            OpeningBalanceCallCount++;
            return Task.FromResult(openingBalance);
        }

        public Task<AccountCardLinePage> GetPageAsync(AccountCardLinePageRequest request, CancellationToken ct = default)
        {
            PageRequests.Add(request);
            return Task.FromResult(page);
        }
    }

    private sealed class StubChartOfAccountsRepository(Guid stubAccountId, string? stubCode) : IChartOfAccountsRepository
    {
        public Task<IReadOnlyList<Account>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Account>>([]);

        public Task<IReadOnlyList<ChartOfAccountsAdminItem>> GetForAdminAsync(bool includeDeleted = false, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ChartOfAccountsAdminItem>>([]);

        public Task<ChartOfAccountsAdminPage> GetAdminPageAsync(ChartOfAccountsAdminPageQuery query, CancellationToken ct = default)
            => throw new NotSupportedException();

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
