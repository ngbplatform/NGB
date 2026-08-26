using FluentAssertions;
using Moq;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Services;
using NGB.Core.Dimensions;
using NGB.Persistence.OperationalRegisters;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;
using NGB.Trade.Runtime.Documents.Validation;
using NGB.Trade.Runtime.Policy;

namespace NGB.Trade.Runtime.Tests.Documents.Validation;

public sealed class TradeInventoryAvailabilityService_P0Tests
{
    private static readonly Guid ItemDimensionId = DeterministicGuid.Create($"Dimension|{TradeCodes.Item}");
    private static readonly Guid WarehouseDimensionId = DeterministicGuid.Create($"Dimension|{TradeCodes.Warehouse}");

    [Fact]
    public async Task EnsureSufficientOnHandAsync_IgnoresEmptyOrNonPositiveWithdrawals()
    {
        var policyReader = new Mock<ITradeAccountingPolicyReader>(MockBehavior.Strict);
        var netReader = new Mock<IOperationalRegisterResourceNetReader>(MockBehavior.Strict);
        var catalogs = new Mock<ICatalogService>(MockBehavior.Strict);
        var sut = CreateSut(policyReader.Object, netReader.Object, catalogs.Object);

        await sut.EnsureSufficientOnHandAsync(
            new DateOnly(2026, 4, 18),
            [
                new TradeInventoryWithdrawalRequest(Guid.NewGuid(), Guid.NewGuid(), 0m),
                new TradeInventoryWithdrawalRequest(Guid.NewGuid(), Guid.NewGuid(), -5m)
            ],
            CancellationToken.None);

        policyReader.VerifyNoOtherCalls();
        netReader.VerifyNoOtherCalls();
        catalogs.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task EnsureSufficientOnHandAsync_BatchesAggregatedKeys_AndUsesCatalogDisplays()
    {
        var registerId = Guid.NewGuid();
        var alphaWarehouseId = Guid.NewGuid();
        var bravoWarehouseId = Guid.NewGuid();
        var cableTiesId = Guid.NewGuid();
        var adapterId = Guid.NewGuid();
        var asOf = new DateOnly(2026, 4, 18);
        var policyReader = PolicyReader(registerId);
        var netReader = new Mock<IOperationalRegisterResourceNetReader>(MockBehavior.Strict);
        netReader
            .Setup(x => x.GetNetsByDimensionsAsync(
                registerId,
                It.IsAny<IReadOnlyList<IReadOnlyList<DimensionValue>>>(),
                "qty_delta",
                asOf,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([0m, 0m]);
        var catalogs = new Mock<ICatalogService>(MockBehavior.Strict);
        catalogs
            .Setup(x => x.GetByIdsAsync(TradeCodes.Item, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, IReadOnlyList<Guid> ids, CancellationToken _) => ids
                .Select(id => new LookupItemDto(id, id == cableTiesId ? "Cable Ties" : "Adapter Kit"))
                .ToArray());
        catalogs
            .Setup(x => x.GetByIdsAsync(TradeCodes.Warehouse, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, IReadOnlyList<Guid> ids, CancellationToken _) => ids
                .Select(id => new LookupItemDto(id, id == alphaWarehouseId ? "Alpha DC" : "Bravo East"))
                .ToArray());
        var sut = CreateSut(policyReader.Object, netReader.Object, catalogs.Object);

        var act = () => sut.EnsureSufficientOnHandAsync(
            asOf,
            [
                new TradeInventoryWithdrawalRequest(alphaWarehouseId, cableTiesId, 2.5m),
                new TradeInventoryWithdrawalRequest(alphaWarehouseId, cableTiesId, 1.5m),
                new TradeInventoryWithdrawalRequest(bravoWarehouseId, adapterId, 3m),
                new TradeInventoryWithdrawalRequest(alphaWarehouseId, adapterId, -2m)
            ],
            CancellationToken.None);

        var error = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        error.Which.ParamName.Should().Be("lines");
        error.Which.Reason.Should().Contain("Alpha DC / Cable Ties: requested 4, available 0.");
        error.Which.Reason.Should().Contain("Bravo East / Adapter Kit: requested 3, available 0.");
        netReader.Verify(x => x.GetNetsByDimensionsAsync(
            registerId,
            It.Is<IReadOnlyList<IReadOnlyList<DimensionValue>>>(groups =>
                groups.Count == 2
                && Contains(groups[0], alphaWarehouseId, cableTiesId)
                && Contains(groups[1], bravoWarehouseId, adapterId)),
            "qty_delta",
            asOf,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnsureSufficientOnHandAsync_WhenBatchBalancesCoverRequests_DoesNotLoadDisplays()
    {
        var registerId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var netReader = new Mock<IOperationalRegisterResourceNetReader>(MockBehavior.Strict);
        netReader.Setup(x => x.GetNetsByDimensionsAsync(
                registerId,
                It.IsAny<IReadOnlyList<IReadOnlyList<DimensionValue>>>(),
                "qty_delta",
                new DateOnly(2026, 4, 18),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([4m]);
        var catalogs = new Mock<ICatalogService>(MockBehavior.Strict);
        var sut = CreateSut(PolicyReader(registerId).Object, netReader.Object, catalogs.Object);

        var act = () => sut.EnsureSufficientOnHandAsync(
            new DateOnly(2026, 4, 18),
            [new TradeInventoryWithdrawalRequest(warehouseId, itemId, 4m)],
            CancellationToken.None);

        await act.Should().NotThrowAsync();
        catalogs.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task EnsureSufficientOnHandAsync_WhenCatalogRowsAreMissing_UsesStableGuidDisplays()
    {
        var registerId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var netReader = new Mock<IOperationalRegisterResourceNetReader>(MockBehavior.Strict);
        netReader.Setup(x => x.GetNetsByDimensionsAsync(
                registerId,
                It.IsAny<IReadOnlyList<IReadOnlyList<DimensionValue>>>(),
                "qty_delta",
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([3m]);
        var catalogs = new Mock<ICatalogService>(MockBehavior.Strict);
        catalogs.Setup(x => x.GetByIdsAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var sut = CreateSut(PolicyReader(registerId).Object, netReader.Object, catalogs.Object);

        var act = () => sut.EnsureSufficientOnHandAsync(
            new DateOnly(2026, 4, 18),
            [new TradeInventoryWithdrawalRequest(warehouseId, itemId, 4m)],
            CancellationToken.None);

        var error = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        error.Which.Reason.Should().Contain(warehouseId.ToString("D")).And.Contain(itemId.ToString("D"));
    }

    private static bool Contains(IReadOnlyList<DimensionValue> dimensions, Guid warehouseId, Guid itemId)
        => dimensions.Contains(new DimensionValue(WarehouseDimensionId, warehouseId))
           && dimensions.Contains(new DimensionValue(ItemDimensionId, itemId));

    private static TradeInventoryAvailabilityService CreateSut(
        ITradeAccountingPolicyReader policyReader,
        IOperationalRegisterResourceNetReader netReader,
        ICatalogService catalogs)
        => new(policyReader, netReader, catalogs);

    private static Mock<ITradeAccountingPolicyReader> PolicyReader(Guid registerId)
    {
        var reader = new Mock<ITradeAccountingPolicyReader>(MockBehavior.Strict);
        reader.Setup(x => x.GetRequiredAsync(It.IsAny<CancellationToken>())).ReturnsAsync(CreatePolicy(registerId));
        return reader;
    }

    private static TradeAccountingPolicy CreatePolicy(Guid registerId)
        => new(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), registerId, Guid.NewGuid());
}
