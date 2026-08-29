using System.Text.Json;
using FluentAssertions;
using Moq;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Reporting;
using NGB.Contracts.Services;
using NGB.Core.Dimensions;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.OperationalRegisters;
using NGB.Runtime.OperationalRegisters;
using NGB.Tools.Extensions;
using NGB.Trade.Runtime.Documents.Validation;
using NGB.Trade.Runtime.Policy;
using NGB.Trade.Runtime.Reporting;

namespace NGB.Trade.Runtime.Tests.Reporting;

public sealed class TradeReportingHelperEdgeCoverageTests
{
    private static readonly Guid ItemDimensionId = DeterministicGuid.Create($"Dimension|{TradeCodes.Item}");
    private static readonly Guid WarehouseDimensionId = DeterministicGuid.Create($"Dimension|{TradeCodes.Warehouse}");

    [Fact]
    public void DimensionHelpers_CoverAbsentAndInvalidFiltersAndMissingDimension()
    {
        var definition = new ReportDefinitionDto("trade.test", "Test");
        var invalidRequest = new ReportExecutionRequestDto(Filters: new Dictionary<string, ReportFilterValueDto>
        {
            ["item_id"] = new(JsonSerializer.SerializeToElement(Guid.Empty))
        });

        TradeReportingHelpers.BuildItemWarehouseFilters(definition, new ReportExecutionRequestDto()).Should().BeEmpty();
        var act = () => TradeReportingHelpers.BuildItemWarehouseFilters(definition, invalidRequest);
        act.Should().Throw<NGB.Core.Reporting.Exceptions.ReportLayoutValidationException>();
        TradeReportingHelpers.GetDisplay(DimensionBag.Empty, new Dictionary<Guid, string>(), TradeCodes.Item)
            .Should().BeEmpty();
        TradeReportingHelpers.TryGetValueId(DimensionBag.Empty, TradeCodes.Item).Should().BeNull();
    }

    [Fact]
    public async Task ReadInventoryBalances_CoversProjectionPaginationReplacementAndNewMovementSnapshots()
    {
        var registerId = Guid.CreateVersion7();
        var projectedId = Guid.CreateVersion7();
        var newId = Guid.CreateVersion7();
        var item = Guid.CreateVersion7();
        var warehouse = Guid.CreateVersion7();
        var projectionCalls = 0;
        var read = new Mock<IOperationalRegisterReadService>(MockBehavior.Strict);
        read.Setup(x => x.GetBalancesPageAsync(
                It.IsAny<OperationalRegisterMonthlyProjectionPageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OperationalRegisterMonthlyProjectionPageRequest request, CancellationToken _) =>
            {
                projectionCalls++;
                return projectionCalls == 1
                    ? new OperationalRegisterMonthlyProjectionPage(
                        request.RegisterId, request.FromInclusive, request.ToInclusive,
                        [Projection(projectedId, new DateOnly(2026, 3, 1), item, warehouse, 5m),
                         Projection(projectedId, new DateOnly(2026, 2, 1), item, warehouse, 99m)],
                        true,
                        new OperationalRegisterMonthlyProjectionPageCursor(new DateOnly(2026, 3, 1), projectedId))
                    : new OperationalRegisterMonthlyProjectionPage(
                        request.RegisterId, request.FromInclusive, request.ToInclusive,
                        [Projection(projectedId, new DateOnly(2026, 3, 1), item, warehouse, 6m)],
                        false,
                        null);
            });
        var movements = new Mock<IOperationalRegisterMovementsQueryReader>(MockBehavior.Strict);
        movements.Setup(x => x.GetByMonthsAsync(
                registerId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<IReadOnlyList<DimensionValue>?>(),
                null, null, null, null, 1000, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                Movement(1, projectedId, item, warehouse, 2m, bagEmpty: false, displaysEmpty: false),
                Movement(2, newId, item, warehouse, 3m, bagEmpty: true, displaysEmpty: true),
                Movement(3, newId, item, warehouse, 4m, bagEmpty: false, displaysEmpty: false)
            ]);

        var result = await TradeReportingHelpers.ReadInventoryBalancesAsync(
            read.Object,
            movements.Object,
            registerId,
            new DateOnly(2026, 4, 18),
            [new DimensionValue(WarehouseDimensionId, warehouse)],
            CancellationToken.None);

        projectionCalls.Should().Be(2);
        result.Single(row => row.DimensionSetId == projectedId).Quantity.Should().Be(8m);
        result.Single(row => row.DimensionSetId == newId).Quantity.Should().Be(7m);
        result.Single(row => row.DimensionSetId == newId).Bag.IsEmpty.Should().BeFalse();
        result.Single(row => row.DimensionSetId == newId).Displays.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ReadInventoryBalances_Rejects_combined_projection_and_movement_cardinality_over_global_bound()
    {
        var registerId = Guid.CreateVersion7();
        var read = new Mock<IOperationalRegisterReadService>(MockBehavior.Strict);
        var snapshots = Enumerable.Range(0, PagingLimits.MaxMaterializedRows)
            .Select(index => Projection(
                Id(index + 1),
                new DateOnly(2026, 3, 1),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                1m))
            .ToArray();
        read.Setup(x => x.GetBalancesPageAsync(
                It.IsAny<OperationalRegisterMonthlyProjectionPageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OperationalRegisterMonthlyProjectionPageRequest request, CancellationToken _) =>
                new OperationalRegisterMonthlyProjectionPage(
                    request.RegisterId,
                    request.FromInclusive,
                    request.ToInclusive,
                    snapshots,
                    false,
                    null));
        var movements = new Mock<IOperationalRegisterMovementsQueryReader>(MockBehavior.Strict);
        movements.Setup(x => x.GetByMonthsAsync(
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
            .ReturnsAsync(
            [
                Movement(
                    1,
                    Id(PagingLimits.MaxMaterializedRows + 1),
                    Guid.CreateVersion7(),
                    Guid.CreateVersion7(),
                    1m,
                    bagEmpty: false,
                    displaysEmpty: false)
            ]);

        await ((Func<Task>)(() => TradeReportingHelpers.ReadInventoryBalancesAsync(
                read.Object,
                movements.Object,
                registerId,
                new DateOnly(2026, 4, 18),
                dimensions: null,
                CancellationToken.None)))
            .Should().ThrowAsync<NGB.Tools.Exceptions.NgbArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task InventoryAvailability_WhenCatalogFallbackIsMissing_UsesStableGuidDisplays()
    {
        var policy = Policy();
        var item = Guid.CreateVersion7();
        var warehouse = Guid.CreateVersion7();
        var policyReader = new Mock<ITradeAccountingPolicyReader>(MockBehavior.Strict);
        policyReader.Setup(x => x.GetRequiredAsync(It.IsAny<CancellationToken>())).ReturnsAsync(policy);
        var netReader = new Mock<IOperationalRegisterResourceNetReader>(MockBehavior.Strict);
        netReader.Setup(x => x.GetNetsByDimensionsAsync(
                policy.InventoryMovementsRegisterId,
                It.IsAny<IReadOnlyList<IReadOnlyList<DimensionValue>>>(),
                "qty_delta",
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([0m]);
        var catalogs = new Mock<ICatalogService>(MockBehavior.Strict);
        catalogs.Setup(x => x.GetByIdsAsync(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var sut = new TradeInventoryAvailabilityService(
            policyReader.Object, netReader.Object, catalogs.Object);
        var act = () => sut.EnsureSufficientOnHandAsync(
            new DateOnly(2026, 4, 18), [new TradeInventoryWithdrawalRequest(warehouse, item, 1m)], default);

        var error = await act.Should().ThrowAsync<NGB.Tools.Exceptions.NgbArgumentInvalidException>();
        error.Which.Reason.Should().Contain(warehouse.ToString("D")).And.Contain(item.ToString("D"));
    }

    [Theory]
    [InlineData("missing-active")]
    [InlineData("missing-key")]
    [InlineData("null-fields")]
    [InlineData("invalid-string")]
    [InlineData("number-true")]
    [InlineData("number-false")]
    [InlineData("non-integer")]
    public async Task CatalogBooleanParsing_CoversMissingStringNumberAndDefaultKinds(string scenario)
    {
        var id = Guid.CreateVersion7();
        IReadOnlyDictionary<string, JsonElement>? fields = scenario switch
        {
            "missing-active" => null,
            "missing-key" => Fields(("unrelated", true)),
            "null-fields" => null,
            "invalid-string" => Fields(("is_inventory_item", "invalid")),
            "number-true" => Fields(("is_inventory_item", 1)),
            "number-false" => Fields(("is_inventory_item", 0)),
            _ => Fields(("is_inventory_item", 1.5m))
        };
        var item = new CatalogItemDto(id, "Item", new RecordPayload(fields), false, false);
        var catalogs = new Mock<ICatalogService>(MockBehavior.Strict);
        catalogs.Setup(x => x.GetByIdAsync(It.IsAny<string>(), id, It.IsAny<CancellationToken>())).ReturnsAsync(item);

        if (scenario == "missing-active")
        {
            await TradeCatalogValidationGuards.EnsureWarehouseAsync(id, "warehouse_id", catalogs.Object, default);
            return;
        }

        var act = () => TradeCatalogValidationGuards.EnsureInventoryItemAsync(id, "item_id", catalogs.Object, default);
        if (scenario == "number-true") await act.Should().NotThrowAsync();
        else await act.Should().ThrowAsync<NGB.Tools.Exceptions.NgbArgumentInvalidException>();
    }

    private static IReadOnlyDictionary<string, JsonElement> Fields(params (string Key, object Value)[] values) =>
        values.ToDictionary(value => value.Key, value => JsonSerializer.SerializeToElement(value.Value));

    private static Guid Id(int value)
    {
        var bytes = new byte[16];
        BitConverter.GetBytes(value).CopyTo(bytes, 0);
        return new Guid(bytes);
    }

    private static OperationalRegisterMonthlyProjectionReadRow Projection(
        Guid id, DateOnly month, Guid item, Guid warehouse, decimal quantity) => new()
    {
        PeriodMonth = month,
        DimensionSetId = id,
        Dimensions = Bag(item, warehouse),
        DimensionValueDisplays = new Dictionary<Guid, string> { [ItemDimensionId] = "Item", [WarehouseDimensionId] = "Warehouse" },
        Values = new Dictionary<string, decimal> { ["qty_delta"] = quantity }
    };

    private static OperationalRegisterMovementQueryReadRow Movement(
        long movementId, Guid dimensionSetId, Guid item, Guid warehouse, decimal quantity,
        bool bagEmpty, bool displaysEmpty) => new()
    {
        MovementId = movementId,
        DocumentId = Guid.CreateVersion7(),
        OccurredAtUtc = new DateTime(2026, 4, 10, 0, 0, 0, DateTimeKind.Utc),
        PeriodMonth = new DateOnly(2026, 4, 1),
        DimensionSetId = dimensionSetId,
        Dimensions = bagEmpty ? DimensionBag.Empty : Bag(item, warehouse),
        DimensionValueDisplays = displaysEmpty
            ? new Dictionary<Guid, string>()
            : new Dictionary<Guid, string> { [ItemDimensionId] = "Item", [WarehouseDimensionId] = "Warehouse" },
        Values = new Dictionary<string, decimal> { ["qty_delta"] = quantity }
    };

    private static DimensionBag Bag(Guid item, Guid warehouse) =>
        new([new DimensionValue(ItemDimensionId, item), new DimensionValue(WarehouseDimensionId, warehouse)]);

    private static TradeAccountingPolicy Policy() =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7());
}
