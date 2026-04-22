using FluentAssertions;
using NGB.Core.Dimensions;
using NGB.Core.Dimensions.Enrichment;
using NGB.Persistence.Dimensions;
using NGB.Persistence.Dimensions.Enrichment;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Reporting;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class TrialBalanceService_P0Tests
{
    [Fact]
    public async Task GetAsync_Materializes_AggregatedSnapshotRows()
    {
        var cashId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var revenueId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var cashSetId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var revenueSetId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        var service = new TrialBalanceService(
            new StubTrialBalanceSnapshotReader(
                new TrialBalanceSnapshot([
                    new TrialBalanceSnapshotRow(cashId, "1000", cashSetId, 100m, 50m, 0m),
                    new TrialBalanceSnapshotRow(revenueId, "4000", revenueSetId, -100m, 0m, 50m)
                ])),
            new StubDimensionSetReader(new Dictionary<Guid, DimensionBag>
            {
                [cashSetId] = DimensionBag.Empty,
                [revenueSetId] = DimensionBag.Empty
            }),
            new StubDimensionValueEnrichmentReader());

        var rows = await service.GetAsync(
            fromInclusive: new DateOnly(2026, 2, 1),
            toInclusive: new DateOnly(2026, 2, 1),
            CancellationToken.None);

        rows.Should().HaveCount(2);

        var cash = rows.Single(x => x.AccountCode == "1000");
        cash.OpeningBalance.Should().Be(100m);
        cash.DebitAmount.Should().Be(50m);
        cash.CreditAmount.Should().Be(0m);
        cash.ClosingBalance.Should().Be(150m);

        var revenue = rows.Single(x => x.AccountCode == "4000");
        revenue.OpeningBalance.Should().Be(-100m);
        revenue.DebitAmount.Should().Be(0m);
        revenue.CreditAmount.Should().Be(50m);
        revenue.ClosingBalance.Should().Be(-150m);
    }

    [Fact]
    public async Task GetAsync_Resolves_DimensionBags_ForAggregatedSnapshotRows()
    {
        var cashId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var revenueId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var dimId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var valueA = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var valueB = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var cashSetA = Guid.Parse("88888888-8888-8888-8888-888888888888");
        var revenueSetA = Guid.Parse("99999999-9999-9999-9999-999999999999");
        var cashSetB = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var revenueSetB = Guid.Parse("ffffffff-1111-2222-3333-444444444444");
        var bagA = new DimensionBag([new DimensionValue(dimId, valueA)]);
        var bagB = new DimensionBag([new DimensionValue(dimId, valueB)]);

        var service = new TrialBalanceService(
            new StubTrialBalanceSnapshotReader(
                new TrialBalanceSnapshot([
                    new TrialBalanceSnapshotRow(cashId, "1000", cashSetA, 100m, 50m, 0m),
                    new TrialBalanceSnapshotRow(revenueId, "4000", revenueSetA, -100m, 0m, 50m)
                ])),
            new StubDimensionSetReader(new Dictionary<Guid, DimensionBag>
            {
                [cashSetA] = bagA,
                [revenueSetA] = bagA,
                [cashSetB] = bagB,
                [revenueSetB] = bagB
            }),
            new StubDimensionValueEnrichmentReader());

        var rows = await service.GetAsync(
            fromInclusive: new DateOnly(2026, 2, 1),
            toInclusive: new DateOnly(2026, 2, 1),
            dimensionScopes: new DimensionScopeBag([new DimensionScope(dimId, [valueA])]),
            ct: CancellationToken.None);

        rows.Should().HaveCount(2);
        rows.Select(x => x.DimensionSetId).Should().BeEquivalentTo([cashSetA, revenueSetA]);

        var cash = rows.Single(x => x.AccountCode == "1000");
        cash.OpeningBalance.Should().Be(100m);
        cash.DebitAmount.Should().Be(50m);
        cash.ClosingBalance.Should().Be(150m);

        var revenue = rows.Single(x => x.AccountCode == "4000");
        revenue.OpeningBalance.Should().Be(-100m);
        revenue.CreditAmount.Should().Be(50m);
        revenue.ClosingBalance.Should().Be(-150m);
    }

    private sealed class StubTrialBalanceSnapshotReader(TrialBalanceSnapshot snapshot) : ITrialBalanceSnapshotReader
    {
        public Task<TrialBalanceSnapshot> GetAsync(
            DateOnly fromInclusive,
            DateOnly toInclusive,
            DimensionScopeBag? dimensionScopes,
            CancellationToken ct = default)
            => Task.FromResult(snapshot);
    }

    private sealed class StubDimensionSetReader(IReadOnlyDictionary<Guid, DimensionBag> bagsById) : IDimensionSetReader
    {
        public Task<IReadOnlyDictionary<Guid, DimensionBag>> GetBagsByIdsAsync(
            IReadOnlyCollection<Guid> dimensionSetIds,
            CancellationToken ct = default)
        {
            var result = dimensionSetIds.ToDictionary(
                id => id,
                id => bagsById.TryGetValue(id, out var bag) ? bag : DimensionBag.Empty);
            return Task.FromResult((IReadOnlyDictionary<Guid, DimensionBag>)result);
        }
    }

    private sealed class StubDimensionValueEnrichmentReader : IDimensionValueEnrichmentReader
    {
        public Task<IReadOnlyDictionary<DimensionValueKey, string>> ResolveAsync(
            IReadOnlyCollection<DimensionValueKey> keys,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<DimensionValueKey, string>>(new Dictionary<DimensionValueKey, string>());
    }
}
