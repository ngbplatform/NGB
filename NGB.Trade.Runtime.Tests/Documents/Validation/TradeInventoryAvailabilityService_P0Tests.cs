using FluentAssertions;
using Moq;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Services;
using NGB.Core.Dimensions;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.OperationalRegisters;
using NGB.Runtime.OperationalRegisters;
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
        var readService = new Mock<IOperationalRegisterReadService>(MockBehavior.Strict);
        var movementsReader = new Mock<IOperationalRegisterMovementsQueryReader>(MockBehavior.Strict);
        var catalogs = new Mock<ICatalogService>(MockBehavior.Strict);
        var sut = CreateSut(policyReader.Object, readService.Object, movementsReader.Object, catalogs.Object);

        await sut.EnsureSufficientOnHandAsync(
            new DateOnly(2026, 4, 18),
            [
                new TradeInventoryWithdrawalRequest(Guid.NewGuid(), Guid.NewGuid(), 0m),
                new TradeInventoryWithdrawalRequest(Guid.NewGuid(), Guid.NewGuid(), -5m)
            ],
            CancellationToken.None);

        policyReader.VerifyNoOtherCalls();
        readService.VerifyNoOtherCalls();
        movementsReader.VerifyNoOtherCalls();
        catalogs.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task EnsureSufficientOnHandAsync_AggregatesShortages_AndUsesCatalogFallbackDisplays()
    {
        var registerId = Guid.NewGuid();
        var alphaWarehouseId = Guid.NewGuid();
        var bravoWarehouseId = Guid.NewGuid();
        var cableTiesId = Guid.NewGuid();
        var adapterId = Guid.NewGuid();

        var policyReader = new Mock<ITradeAccountingPolicyReader>();
        policyReader
            .Setup(x => x.GetRequiredAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePolicy(registerId));

        var readService = new Mock<IOperationalRegisterReadService>();
        readService
            .Setup(x => x.GetBalancesPageAsync(It.IsAny<OperationalRegisterMonthlyProjectionPageRequest>(), It.IsAny<CancellationToken>()))
            .Returns<OperationalRegisterMonthlyProjectionPageRequest, CancellationToken>((request, _) =>
            {
                return Task.FromResult(new OperationalRegisterMonthlyProjectionPage(
                    request.RegisterId,
                    request.FromInclusive,
                    request.ToInclusive,
                    Array.Empty<OperationalRegisterMonthlyProjectionReadRow>(),
                    HasMore: false,
                    NextCursor: null));
            });

        var movementsReader = new Mock<IOperationalRegisterMovementsQueryReader>();
        movementsReader
            .Setup(x => x.GetByMonthsAsync(
                registerId,
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<IReadOnlyList<DimensionValue>?>(),
                null,
                null,
                null,
                null,
                1000,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<OperationalRegisterMovementQueryReadRow>());

        var catalogs = new Mock<ICatalogService>();
        catalogs
            .Setup(x => x.GetByIdsAsync(TradeCodes.Item, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, IReadOnlyList<Guid> ids, CancellationToken _) =>
                ids.Select(id => new LookupItemDto(
                    id,
                    id == cableTiesId ? "Cable Ties" : "Adapter Kit"))
                .ToArray());
        catalogs
            .Setup(x => x.GetByIdsAsync(TradeCodes.Warehouse, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, IReadOnlyList<Guid> ids, CancellationToken _) =>
                ids.Select(id => new LookupItemDto(
                    id,
                    id == alphaWarehouseId ? "Alpha DC" : "Bravo East"))
                .ToArray());

        var sut = CreateSut(policyReader.Object, readService.Object, movementsReader.Object, catalogs.Object);

        Func<Task> act = () => sut.EnsureSufficientOnHandAsync(
            new DateOnly(2026, 4, 18),
            [
                new TradeInventoryWithdrawalRequest(alphaWarehouseId, cableTiesId, 2.5m),
                new TradeInventoryWithdrawalRequest(alphaWarehouseId, cableTiesId, 1.5m),
                new TradeInventoryWithdrawalRequest(bravoWarehouseId, adapterId, 3m),
                new TradeInventoryWithdrawalRequest(alphaWarehouseId, adapterId, -2m)
            ],
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("lines");
        ex.Which.Reason.Should().Contain("Insufficient inventory on hand as of 2026-04-18.");
        ex.Which.Reason.Should().Contain("Alpha DC / Cable Ties: requested 4, available 0.");
        ex.Which.Reason.Should().Contain("Bravo East / Adapter Kit: requested 3, available 0.");
        ex.Which.Reason.IndexOf("Alpha DC / Cable Ties", StringComparison.Ordinal)
            .Should().BeLessThan(ex.Which.Reason.IndexOf("Bravo East / Adapter Kit", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EnsureSufficientOnHandAsync_UsesCurrentMonthMovements_ToCloseTheGap()
    {
        var registerId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var dimensionSetId = Guid.NewGuid();

        var policyReader = new Mock<ITradeAccountingPolicyReader>();
        policyReader
            .Setup(x => x.GetRequiredAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePolicy(registerId));

        var readService = new Mock<IOperationalRegisterReadService>();
        readService
            .Setup(x => x.GetBalancesPageAsync(It.IsAny<OperationalRegisterMonthlyProjectionPageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OperationalRegisterMonthlyProjectionPageRequest request, CancellationToken _) =>
                new OperationalRegisterMonthlyProjectionPage(
                    request.RegisterId,
                    request.FromInclusive,
                    request.ToInclusive,
                    [CreateBalanceRow(warehouseId, itemId, 2m, dimensionSetId, warehouseDisplay: "North Hub", itemDisplay: "Panel Kit")],
                    HasMore: false,
                    NextCursor: null));

        var movementsReader = new Mock<IOperationalRegisterMovementsQueryReader>();
        movementsReader
            .Setup(x => x.GetByMonthsAsync(
                registerId,
                new DateOnly(2026, 4, 1),
                new DateOnly(2026, 4, 1),
                It.IsAny<IReadOnlyList<DimensionValue>?>(),
                null,
                null,
                null,
                null,
                1000,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                CreateMovementRow(1, warehouseId, itemId, 2m, new DateTime(2026, 4, 10, 0, 0, 0, DateTimeKind.Utc), dimensionSetId: dimensionSetId)
            ]);

        var sut = CreateSut(policyReader.Object, readService.Object, movementsReader.Object, Mock.Of<ICatalogService>());

        var act = () => sut.EnsureSufficientOnHandAsync(
            new DateOnly(2026, 4, 18),
            [
                new TradeInventoryWithdrawalRequest(warehouseId, itemId, 4m)
            ],
            CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task EnsureSufficientOnHandAsync_AppliesStornoMovements_AndIgnoresFutureMovements()
    {
        var registerId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var dimensionSetId = Guid.NewGuid();

        var policyReader = new Mock<ITradeAccountingPolicyReader>();
        policyReader
            .Setup(x => x.GetRequiredAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePolicy(registerId));

        var readService = new Mock<IOperationalRegisterReadService>();
        readService
            .Setup(x => x.GetBalancesPageAsync(It.IsAny<OperationalRegisterMonthlyProjectionPageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OperationalRegisterMonthlyProjectionPageRequest request, CancellationToken _) =>
                new OperationalRegisterMonthlyProjectionPage(
                    request.RegisterId,
                    request.FromInclusive,
                    request.ToInclusive,
                    [CreateBalanceRow(warehouseId, itemId, 5m, dimensionSetId, warehouseDisplay: "North Hub", itemDisplay: "Panel Kit")],
                    HasMore: false,
                    NextCursor: null));

        var movementsReader = new Mock<IOperationalRegisterMovementsQueryReader>();
        movementsReader
            .Setup(x => x.GetByMonthsAsync(
                registerId,
                new DateOnly(2026, 4, 1),
                new DateOnly(2026, 4, 1),
                It.IsAny<IReadOnlyList<DimensionValue>?>(),
                null,
                null,
                null,
                null,
                1000,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                CreateMovementRow(1, warehouseId, itemId, 2m, new DateTime(2026, 4, 10, 0, 0, 0, DateTimeKind.Utc), isStorno: true, dimensionSetId: dimensionSetId),
                CreateMovementRow(2, warehouseId, itemId, 99m, new DateTime(2026, 4, 25, 0, 0, 0, DateTimeKind.Utc), dimensionSetId: dimensionSetId)
            ]);

        var catalogs = new Mock<ICatalogService>();
        catalogs
            .Setup(x => x.GetByIdsAsync(TradeCodes.Item, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new LookupItemDto(itemId, "Panel Kit")]);
        catalogs
            .Setup(x => x.GetByIdsAsync(TradeCodes.Warehouse, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new LookupItemDto(warehouseId, "North Hub")]);

        var sut = CreateSut(policyReader.Object, readService.Object, movementsReader.Object, catalogs.Object);

        Func<Task> act = () => sut.EnsureSufficientOnHandAsync(
            new DateOnly(2026, 4, 18),
            [
                new TradeInventoryWithdrawalRequest(warehouseId, itemId, 4m)
            ],
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.Reason.Should().Contain("North Hub / Panel Kit: requested 4, available 3.");
    }

    private static TradeInventoryAvailabilityService CreateSut(
        ITradeAccountingPolicyReader? policyReader = null,
        IOperationalRegisterReadService? readService = null,
        IOperationalRegisterMovementsQueryReader? movementsReader = null,
        ICatalogService? catalogs = null)
        => new(
            policyReader ?? Mock.Of<ITradeAccountingPolicyReader>(),
            readService ?? Mock.Of<IOperationalRegisterReadService>(),
            movementsReader ?? Mock.Of<IOperationalRegisterMovementsQueryReader>(),
            catalogs ?? Mock.Of<ICatalogService>());

    private static TradeAccountingPolicy CreatePolicy(Guid registerId)
        => new(
            PolicyId: Guid.NewGuid(),
            CashAccountId: Guid.NewGuid(),
            AccountsReceivableAccountId: Guid.NewGuid(),
            InventoryAccountId: Guid.NewGuid(),
            AccountsPayableAccountId: Guid.NewGuid(),
            SalesRevenueAccountId: Guid.NewGuid(),
            CostOfGoodsSoldAccountId: Guid.NewGuid(),
            InventoryAdjustmentAccountId: Guid.NewGuid(),
            InventoryMovementsRegisterId: registerId,
            ItemPricesRegisterId: Guid.NewGuid());

    private static OperationalRegisterMonthlyProjectionReadRow CreateBalanceRow(
        Guid warehouseId,
        Guid itemId,
        decimal quantity,
        Guid? dimensionSetId = null,
        string? warehouseDisplay = null,
        string? itemDisplay = null)
    {
        var displays = new Dictionary<Guid, string>();
        if (!string.IsNullOrWhiteSpace(warehouseDisplay))
            displays[WarehouseDimensionId] = warehouseDisplay;
        if (!string.IsNullOrWhiteSpace(itemDisplay))
            displays[ItemDimensionId] = itemDisplay;

        return new OperationalRegisterMonthlyProjectionReadRow
        {
            PeriodMonth = new DateOnly(2026, 3, 1),
            DimensionSetId = dimensionSetId ?? Guid.NewGuid(),
            Dimensions = new DimensionBag(
            [
                new DimensionValue(ItemDimensionId, itemId),
                new DimensionValue(WarehouseDimensionId, warehouseId)
            ]),
            DimensionValueDisplays = displays,
            Values = new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["qty_delta"] = quantity
            }
        };
    }

    private static OperationalRegisterMovementQueryReadRow CreateMovementRow(
        long movementId,
        Guid warehouseId,
        Guid itemId,
        decimal quantityDelta,
        DateTime occurredAtUtc,
        bool isStorno = false,
        Guid? dimensionSetId = null)
        => new()
        {
            MovementId = movementId,
            DocumentId = Guid.NewGuid(),
            OccurredAtUtc = occurredAtUtc,
            PeriodMonth = new DateOnly(occurredAtUtc.Year, occurredAtUtc.Month, 1),
            DimensionSetId = dimensionSetId ?? Guid.NewGuid(),
            IsStorno = isStorno,
            Dimensions = new DimensionBag(
            [
                new DimensionValue(ItemDimensionId, itemId),
                new DimensionValue(WarehouseDimensionId, warehouseId)
            ]),
            DimensionValueDisplays = new Dictionary<Guid, string>(),
            Values = new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["qty_delta"] = quantityDelta
            }
        };
}
