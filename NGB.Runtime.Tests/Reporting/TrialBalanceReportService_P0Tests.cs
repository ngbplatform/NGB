using FluentAssertions;
using NGB.Accounting.Accounts;
using NGB.Accounting.Reports.TrialBalance;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Reporting;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class TrialBalanceReportService_P0Tests
{
    [Fact]
    public async Task GetPageAsync_Shapes_Single_Account_Column_Grouping_And_Subtotals_Without_Dimension_Columns()
    {
        var cashId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var arId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var set1 = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var set2 = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var set3 = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

        var snapshotReader = new StubTrialBalanceSnapshotReader(
            new TrialBalanceSnapshot([
                new TrialBalanceSnapshotRow(cashId, "1000", set1, 10m, 5m, 1m),
                new TrialBalanceSnapshotRow(cashId, "1000", set2, 2m, 7m, 4m),
                new TrialBalanceSnapshotRow(arId, "1100", set3, 3m, 6m, 2m)
            ]));

        var accounts = new StubAccountByIdResolver(
            new Account(cashId, "1000", "Operating Cash", AccountType.Asset),
            new Account(arId, "1100", "Accounts Receivable - Tenants", AccountType.Asset));

        var service = new TrialBalanceReportService(snapshotReader, accounts);

        var page = await service.GetPageAsync(
            new TrialBalanceReportPageRequest
            {
                FromInclusive = new DateOnly(2026, 3, 1),
                ToInclusive = new DateOnly(2026, 3, 1),
                Offset = 0,
                Limit = 20,
                ShowSubtotals = true
            },
            CancellationToken.None);

        page.Total.Should().Be(4);
        page.HasMore.Should().BeFalse();
        page.Rows.Select(x => (x.RowKind, x.AccountDisplay)).Should().Equal(
            (TrialBalanceReportRowKind.Group, "Asset"),
            (TrialBalanceReportRowKind.Detail, "1000 — Operating Cash"),
            (TrialBalanceReportRowKind.Detail, "1100 — Accounts Receivable - Tenants"),
            (TrialBalanceReportRowKind.Subtotal, "Asset subtotal"));

        page.Rows[1].OpeningBalance.Should().Be(12m);
        page.Rows[1].DebitAmount.Should().Be(12m);
        page.Rows[1].CreditAmount.Should().Be(5m);
        page.Rows[1].ClosingBalance.Should().Be(19m);

        page.Rows[3].OpeningBalance.Should().Be(15m);
        page.Rows[3].DebitAmount.Should().Be(18m);
        page.Rows[3].CreditAmount.Should().Be(7m);
        page.Rows[3].ClosingBalance.Should().Be(26m);

        page.Totals.Should().Be(new TrialBalanceReportTotals(15m, 18m, 7m, 26m));
        accounts.GetByIdsCallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetPageAsync_Ignores_Offset_And_Limit_And_Returns_Full_Bounded_Row_Model()
    {
        var cashId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var arId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var set1 = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        var set2 = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");

        var snapshotReader = new StubTrialBalanceSnapshotReader(
            new TrialBalanceSnapshot([
                new TrialBalanceSnapshotRow(cashId, "1000", set1, 10m, 3m, 1m),
                new TrialBalanceSnapshotRow(arId, "1100", set2, 5m, 2m, 0m)
            ]));

        var accounts = new StubAccountByIdResolver(
            new Account(cashId, "1000", "Operating Cash", AccountType.Asset),
            new Account(arId, "1100", "Accounts Receivable - Tenants", AccountType.Asset));

        var service = new TrialBalanceReportService(snapshotReader, accounts);

        var page = await service.GetPageAsync(
            new TrialBalanceReportPageRequest
            {
                FromInclusive = new DateOnly(2026, 3, 1),
                ToInclusive = new DateOnly(2026, 3, 1),
                Offset = 99,
                Limit = 1,
                ShowSubtotals = false
            },
            CancellationToken.None);

        page.Total.Should().Be(3);
        page.HasMore.Should().BeFalse();
        page.Rows.Select(x => (x.RowKind, x.AccountDisplay)).Should().Equal(
            (TrialBalanceReportRowKind.Group, "Asset"),
            (TrialBalanceReportRowKind.Detail, "1000 — Operating Cash"),
            (TrialBalanceReportRowKind.Detail, "1100 — Accounts Receivable - Tenants"));
    }

    [Fact]
    public async Task GetPageAsync_Large_Synthetic_Dataset_Returns_Full_Bounded_Summary()
    {
        var snapshotRows = new List<TrialBalanceSnapshotRow>();
        var accounts = new List<Account>();

        for (var i = 0; i < 120; i++)
        {
            var assetId = Guid.CreateVersion7();
            var expenseId = Guid.CreateVersion7();
            var assetSetId = Guid.CreateVersion7();
            var expenseSetId = Guid.CreateVersion7();
            var assetCode = $"1{i:000}";
            var expenseCode = $"5{i:000}";

            accounts.Add(new Account(assetId, assetCode, $"Asset {i}", AccountType.Asset));
            accounts.Add(new Account(expenseId, expenseCode, $"Expense {i}", AccountType.Expense));

            snapshotRows.Add(new TrialBalanceSnapshotRow(assetId, assetCode, assetSetId, 10m, 2m, 1m));
            snapshotRows.Add(new TrialBalanceSnapshotRow(expenseId, expenseCode, expenseSetId, 0m, 3m, 0m));
        }

        var service = new TrialBalanceReportService(
            new StubTrialBalanceSnapshotReader(new TrialBalanceSnapshot(snapshotRows)),
            new StubAccountByIdResolver(accounts.ToArray()));

        var page = await service.GetPageAsync(
            new TrialBalanceReportPageRequest
            {
                FromInclusive = new DateOnly(2026, 3, 1),
                ToInclusive = new DateOnly(2026, 3, 1),
                Offset = 500,
                Limit = 10,
                ShowSubtotals = true
            },
            CancellationToken.None);

        page.Rows.Should().HaveCount(244);
        page.Total.Should().Be(244);
        page.HasMore.Should().BeFalse();
        page.Totals.Should().Be(new TrialBalanceReportTotals(1200m, 600m, 120m, 1680m));
        page.Rows.First().Should().BeEquivalentTo(new { RowKind = TrialBalanceReportRowKind.Group, AccountDisplay = "Asset" });
        page.Rows.Last().Should().BeEquivalentTo(new { RowKind = TrialBalanceReportRowKind.Subtotal, AccountDisplay = "Expense subtotal" });
    }

    [Fact]
    public async Task GetPageAsync_Omits_Subtotal_Rows_When_ShowSubtotals_Is_False()
    {
        var expenseId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var set1 = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

        var snapshotReader = new StubTrialBalanceSnapshotReader(
            new TrialBalanceSnapshot([
                new TrialBalanceSnapshotRow(expenseId, "5300", set1, 0m, 7m, 0m)
            ]));

        var accounts = new StubAccountByIdResolver(
            new Account(expenseId, "5300", "Cleaning Expense", AccountType.Expense));

        var service = new TrialBalanceReportService(snapshotReader, accounts);

        var page = await service.GetPageAsync(
            new TrialBalanceReportPageRequest
            {
                FromInclusive = new DateOnly(2026, 3, 1),
                ToInclusive = new DateOnly(2026, 3, 1),
                Offset = 0,
                Limit = 20,
                ShowSubtotals = false
            },
            CancellationToken.None);

        page.Rows.Select(x => x.RowKind).Should().Equal(TrialBalanceReportRowKind.Group, TrialBalanceReportRowKind.Detail);
    }

    private sealed class StubTrialBalanceSnapshotReader(TrialBalanceSnapshot snapshot) : ITrialBalanceSnapshotReader
    {
        public Task<TrialBalanceSnapshot> GetAsync(
            DateOnly fromInclusive,
            DateOnly toInclusive,
            NGB.Core.Dimensions.DimensionScopeBag? dimensionScopes,
            CancellationToken ct = default)
            => Task.FromResult(snapshot);
    }

    private sealed class StubAccountByIdResolver(params Account[] accounts) : IAccountByIdResolver
    {
        private readonly Dictionary<Guid, Account> _map = accounts.ToDictionary(x => x.Id, x => x);
        public int GetByIdsCallCount { get; private set; }

        public Task<Account?> GetByIdAsync(Guid accountId, CancellationToken ct = default)
            => Task.FromResult(_map.TryGetValue(accountId, out var account) ? account : null);

        public Task<IReadOnlyDictionary<Guid, Account>> GetByIdsAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken ct = default)
        {
            GetByIdsCallCount++;
            var result = accountIds.Where(_map.ContainsKey).ToDictionary(x => x, x => _map[x]);
            return Task.FromResult((IReadOnlyDictionary<Guid, Account>)result);
        }
    }
}
