using FluentAssertions;
using NGB.Accounting.Accounts;
using NGB.Accounting.Reports.GeneralLedgerAggregated;
using NGB.Core.Dimensions;
using NGB.Persistence.Accounts;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Reporting;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class GeneralLedgerAggregatedReportService_P0Tests
{
    [Fact]
    public async Task GetPageAsync_Preserves_Running_Balance_Continuation_Across_Cursor_Pages()
    {
        var accountId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var counterId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var doc1 = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var doc2 = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var set1 = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var set2 = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

        var pageReader = new StubGeneralLedgerAggregatedPageReader(accountId, counterId, doc1, doc2, set1, set2);
        var snapshotReader = new StubGeneralLedgerAggregatedSnapshotReader(
            new GeneralLedgerAggregatedSnapshot("1000", 10m, 7m, 2m));

        var service = new GeneralLedgerAggregatedReportService(
            pageReader,
            snapshotReader,
            new StubChartOfAccountsRepository(accountId, "1000"));

        var page1 = await service.GetPageAsync(
            new GeneralLedgerAggregatedReportPageRequest
            {
                AccountId = accountId,
                FromInclusive = new DateOnly(2026, 3, 1),
                ToInclusive = new DateOnly(2026, 3, 1),
                PageSize = 1
            },
            CancellationToken.None);

        page1.OpeningBalance.Should().Be(10m);
        page1.TotalDebit.Should().Be(7m);
        page1.TotalCredit.Should().Be(2m);
        page1.HasMore.Should().BeTrue();
        page1.NextCursor.Should().NotBeNull();
        page1.Lines.Should().ContainSingle();
        page1.Lines[0].RunningBalance.Should().Be(15m);
        snapshotReader.CallCount.Should().Be(1);

        var page2 = await service.GetPageAsync(
            new GeneralLedgerAggregatedReportPageRequest
            {
                AccountId = accountId,
                FromInclusive = new DateOnly(2026, 3, 1),
                ToInclusive = new DateOnly(2026, 3, 1),
                PageSize = 1,
                Cursor = page1.NextCursor
            },
            CancellationToken.None);

        page2.OpeningBalance.Should().Be(15m);
        page2.HasMore.Should().BeFalse();
        page2.NextCursor.Should().BeNull();
        page2.Lines.Should().ContainSingle();
        page2.Lines[0].RunningBalance.Should().Be(15m);
        page2.ClosingBalance.Should().Be(15m);
        snapshotReader.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetPageAsync_LegacyCursorWithoutEmbeddedTotals_RecomputesRangeTotalsAgainstTrueReportOpening()
    {
        var accountId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var counterId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var doc1 = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var doc2 = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var set1 = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var set2 = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

        var pageReader = new StubGeneralLedgerAggregatedPageReader(accountId, counterId, doc1, doc2, set1, set2);
        var snapshotReader = new StubGeneralLedgerAggregatedSnapshotReader(
            new GeneralLedgerAggregatedSnapshot("1000", 10m, 7m, 2m));

        var service = new GeneralLedgerAggregatedReportService(
            pageReader,
            snapshotReader,
            new StubChartOfAccountsRepository(accountId, "1000"));

        var page = await service.GetPageAsync(
            new GeneralLedgerAggregatedReportPageRequest
            {
                AccountId = accountId,
                FromInclusive = new DateOnly(2026, 3, 1),
                ToInclusive = new DateOnly(2026, 3, 1),
                PageSize = 1,
                Cursor = new GeneralLedgerAggregatedReportCursor
                {
                    AfterPeriodUtc = new DateTime(2026, 3, 5, 0, 0, 0, DateTimeKind.Utc),
                    AfterDocumentId = doc1,
                    AfterCounterAccountCode = "4000",
                    AfterCounterAccountId = counterId,
                    AfterDimensionSetId = set1,
                    RunningBalance = 15m
                }
            },
            CancellationToken.None);

        page.OpeningBalance.Should().Be(15m);
        page.TotalDebit.Should().Be(7m);
        page.TotalCredit.Should().Be(2m);
        page.ClosingBalance.Should().Be(15m);
        snapshotReader.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetPageAsync_WhenPagingIsDisabled_Propagates_Unpaged_Mode_To_LowLevel_Reader()
    {
        var accountId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var counterId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var doc1 = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var doc2 = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var set1 = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var set2 = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

        var pageReader = new StubGeneralLedgerAggregatedPageReader(accountId, counterId, doc1, doc2, set1, set2);
        var snapshotReader = new StubGeneralLedgerAggregatedSnapshotReader(
            new GeneralLedgerAggregatedSnapshot("1000", 10m, 7m, 2m));

        var service = new GeneralLedgerAggregatedReportService(
            pageReader,
            snapshotReader,
            new StubChartOfAccountsRepository(accountId, "1000"));

        var page = await service.GetPageAsync(
            new GeneralLedgerAggregatedReportPageRequest
            {
                AccountId = accountId,
                FromInclusive = new DateOnly(2026, 3, 1),
                ToInclusive = new DateOnly(2026, 3, 1),
                PageSize = 1,
                Cursor = new GeneralLedgerAggregatedReportCursor
                {
                    AfterPeriodUtc = new DateTime(2026, 3, 5, 0, 0, 0, DateTimeKind.Utc),
                    AfterDocumentId = doc1,
                    AfterCounterAccountCode = "4000",
                    AfterCounterAccountId = counterId,
                    AfterDimensionSetId = set1,
                    RunningBalance = 15m,
                    TotalDebit = 7m,
                    TotalCredit = 2m,
                    ClosingBalance = 15m
                },
                DisablePaging = true
            },
            CancellationToken.None);

        pageReader.LastRequest.Should().NotBeNull();
        pageReader.LastRequest!.DisablePaging.Should().BeTrue();
        pageReader.LastRequest.Cursor.Should().BeNull();
        page.HasMore.Should().BeFalse();
    }

    private sealed class StubGeneralLedgerAggregatedPageReader(
        Guid accountId,
        Guid counterId,
        Guid doc1,
        Guid doc2,
        Guid set1,
        Guid set2)
        : IGeneralLedgerAggregatedPageReader
    {
        public GeneralLedgerAggregatedPageRequest? LastRequest { get; private set; }

        public Task<GeneralLedgerAggregatedPage> GetPageAsync(GeneralLedgerAggregatedPageRequest request, CancellationToken ct = default)
        {
            LastRequest = request;

            if (request.DisablePaging)
            {
                request.Cursor.Should().BeNull();

                return Task.FromResult(
                    new GeneralLedgerAggregatedPage(
                        [
                            new GeneralLedgerAggregatedLine
                            {
                                PeriodUtc = new DateTime(2026, 3, 5, 0, 0, 0, DateTimeKind.Utc),
                                DocumentId = doc1,
                                AccountId = accountId,
                                AccountCode = "1000",
                                CounterAccountId = counterId,
                                CounterAccountCode = "4000",
                                DimensionSetId = set1,
                                Dimensions = DimensionBag.Empty,
                                DebitAmount = 5m,
                                CreditAmount = 0m
                            },
                            new GeneralLedgerAggregatedLine
                            {
                                PeriodUtc = new DateTime(2026, 3, 6, 0, 0, 0, DateTimeKind.Utc),
                                DocumentId = doc2,
                                AccountId = accountId,
                                AccountCode = "1000",
                                CounterAccountId = counterId,
                                CounterAccountCode = "4000",
                                DimensionSetId = set2,
                                Dimensions = DimensionBag.Empty,
                                DebitAmount = 2m,
                                CreditAmount = 2m
                            }
                        ],
                        false,
                        null));
            }

            if (request.Cursor is null)
            {
                return Task.FromResult(
                    new GeneralLedgerAggregatedPage(
                        [
                            new GeneralLedgerAggregatedLine
                            {
                                PeriodUtc = new DateTime(2026, 3, 5, 0, 0, 0, DateTimeKind.Utc),
                                DocumentId = doc1,
                                AccountId = accountId,
                                AccountCode = "1000",
                                CounterAccountId = counterId,
                                CounterAccountCode = "4000",
                                DimensionSetId = set1,
                                Dimensions = DimensionBag.Empty,
                                DebitAmount = 5m,
                                CreditAmount = 0m
                            }
                        ],
                        true,
                        new GeneralLedgerAggregatedLineCursor
                        {
                            AfterPeriodUtc = new DateTime(2026, 3, 5, 0, 0, 0, DateTimeKind.Utc),
                            AfterDocumentId = doc1,
                            AfterCounterAccountCode = "4000",
                            AfterCounterAccountId = counterId,
                            AfterDimensionSetId = set1
                        }));
            }

            request.Cursor.AfterDocumentId.Should().Be(doc1);
            return Task.FromResult(
                new GeneralLedgerAggregatedPage(
                    [
                        new GeneralLedgerAggregatedLine
                        {
                            PeriodUtc = new DateTime(2026, 3, 6, 0, 0, 0, DateTimeKind.Utc),
                            DocumentId = doc2,
                            AccountId = accountId,
                            AccountCode = "1000",
                            CounterAccountId = counterId,
                            CounterAccountCode = "4000",
                            DimensionSetId = set2,
                            Dimensions = DimensionBag.Empty,
                            DebitAmount = 2m,
                            CreditAmount = 2m
                        }
                    ],
                    false,
                    null));
        }
    }

    private sealed class StubGeneralLedgerAggregatedSnapshotReader(GeneralLedgerAggregatedSnapshot snapshot)
        : IGeneralLedgerAggregatedSnapshotReader
    {
        public int CallCount { get; private set; }

        public Task<GeneralLedgerAggregatedSnapshot> GetAsync(
            Guid accountId,
            DateOnly fromInclusive,
            DateOnly toInclusive,
            DimensionScopeBag? dimensionScopes,
            CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult(snapshot);
        }
    }

    private sealed class StubChartOfAccountsRepository(Guid accountId, string code) : IChartOfAccountsRepository
    {
        public Task<IReadOnlyList<Account>> GetAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Account>>([]);
        public Task<IReadOnlyList<ChartOfAccountsAdminItem>> GetForAdminAsync(bool includeDeleted = false, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ChartOfAccountsAdminItem>>([]);
        public Task<ChartOfAccountsAdminItem?> GetAdminByIdAsync(Guid accountId, CancellationToken ct = default) => Task.FromResult<ChartOfAccountsAdminItem?>(null);
        public Task<IReadOnlyList<ChartOfAccountsAdminItem>> GetAdminByIdsAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ChartOfAccountsAdminItem>>([]);
        public Task<bool> HasMovementsAsync(Guid accountId, CancellationToken ct = default) => Task.FromResult(false);
        public Task CreateAsync(Account account, bool isActive = true, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string?> GetCodeByIdAsync(Guid requestedAccountId, CancellationToken ct = default) => Task.FromResult<string?>(requestedAccountId == accountId ? code : null);
        public Task UpdateAsync(Account account, bool isActive, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetActiveAsync(Guid accountId, bool isActive, CancellationToken ct = default) => Task.CompletedTask;
        public Task MarkForDeletionAsync(Guid accountId, CancellationToken ct = default) => Task.CompletedTask;
        public Task UnmarkForDeletionAsync(Guid accountId, CancellationToken ct = default) => Task.CompletedTask;
    }
}
