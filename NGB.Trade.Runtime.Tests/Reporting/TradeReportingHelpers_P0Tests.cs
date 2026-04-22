using System.Text.Json;
using FluentAssertions;
using Moq;
using NGB.Contracts.Reporting;
using NGB.Core.Dimensions;
using NGB.OperationalRegisters.Contracts;
using NGB.Core.Reporting.Exceptions;
using NGB.Persistence.OperationalRegisters;
using NGB.Tools.Extensions;
using NGB.Trade.Runtime.Reporting;
using NGB.Trade.Runtime.Tests.Infrastructure;

namespace NGB.Trade.Runtime.Tests.Reporting;

public sealed class TradeReportingHelpers_P0Tests
{
    [Fact]
    public void BuildItemWarehouseFilters_ReturnsDimensionFilters_ForNonEmptyIds()
    {
        var itemId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();

        var filters = TradeReportingHelpers.BuildItemWarehouseFilters(
            new ReportDefinitionDto("trd.inventory_balances", "Inventory Balances"),
            new ReportExecutionRequestDto(
                Filters: new Dictionary<string, ReportFilterValueDto>(StringComparer.OrdinalIgnoreCase)
                {
                    ["item_id"] = new(JsonSerializer.SerializeToElement(itemId)),
                    ["warehouse_id"] = new(JsonSerializer.SerializeToElement(warehouseId))
                }));

        filters.Should().BeEquivalentTo(
        [
            new DimensionValue(DeterministicGuid.Create($"Dimension|{TradeCodes.Item}"), itemId),
            new DimensionValue(DeterministicGuid.Create($"Dimension|{TradeCodes.Warehouse}"), warehouseId)
        ]);
    }

    [Fact]
    public void GetDateRangeOrCurrentMonth_UsesCurrentUtcMonth_WhenParametersAreMissing()
    {
        var definition = new ReportDefinitionDto("trd.sales_by_item", "Sales by Item");
        var request = new ReportExecutionRequestDto();
        var timeProvider = new TestTimeProvider(new DateTimeOffset(2026, 4, 18, 12, 0, 0, TimeSpan.Zero));

        var (fromInclusive, toInclusive) = TradeReportingHelpers.GetDateRangeOrCurrentMonth(definition, request, timeProvider);

        fromInclusive.Should().Be(new DateOnly(2026, 4, 1));
        toInclusive.Should().Be(new DateOnly(2026, 4, 18));
    }

    [Fact]
    public void GetDateRangeOrCurrentMonth_WhenRangeIsInverted_ThrowsValidationException()
    {
        var definition = new ReportDefinitionDto("trd.sales_by_item", "Sales by Item");
        var request = new ReportExecutionRequestDto(
            Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["from_utc"] = "2026-04-20",
                ["to_utc"] = "2026-04-18"
            });

        var act = () => TradeReportingHelpers.GetDateRangeOrCurrentMonth(
            definition,
            request,
            new TestTimeProvider(new DateTimeOffset(2026, 4, 18, 0, 0, 0, TimeSpan.Zero)));

        var ex = act.Should().Throw<ReportLayoutValidationException>();
        ex.Which.Message.Should().Contain("must be on or after");
    }

    [Fact]
    public async Task ReadAllMovementsAsync_PaginatesUntilShortPage_UsingMovementCursor()
    {
        var registerId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var afterMovementIds = new List<long?>();

        var reader = new Mock<IOperationalRegisterMovementsQueryReader>();
        reader
            .Setup(x => x.GetByMonthsAsync(
                registerId,
                new DateOnly(2026, 4, 1),
                new DateOnly(2026, 4, 30),
                null,
                null,
                null,
                null,
                It.IsAny<long?>(),
                1000,
                It.IsAny<CancellationToken>()))
            .Returns<Guid, DateOnly, DateOnly, IReadOnlyList<DimensionValue>?, Guid?, Guid?, bool?, long?, int, CancellationToken>((_, _, _, _, _, _, _, afterMovementId, _, _) =>
            {
                afterMovementIds.Add(afterMovementId);

                if (afterMovementId is null)
                {
                    var firstPage = Enumerable.Range(1, 1000)
                        .Select(index => CreateMovementRow(index, warehouseId, itemId))
                        .ToArray();
                    return Task.FromResult<IReadOnlyList<OperationalRegisterMovementQueryReadRow>>(firstPage);
                }

                return Task.FromResult<IReadOnlyList<OperationalRegisterMovementQueryReadRow>>(
                [
                    CreateMovementRow(1001, warehouseId, itemId),
                    CreateMovementRow(1002, warehouseId, itemId)
                ]);
            });

        var rows = await TradeReportingHelpers.ReadAllMovementsAsync(
            reader.Object,
            registerId,
            new DateOnly(2026, 4, 1),
            new DateOnly(2026, 4, 30),
            dimensions: null,
            CancellationToken.None);

        rows.Should().HaveCount(1002);
        rows.First().MovementId.Should().Be(1);
        rows.Last().MovementId.Should().Be(1002);
        afterMovementIds.Should().Equal(null, 1000L);
    }

    private static OperationalRegisterMovementQueryReadRow CreateMovementRow(long movementId, Guid warehouseId, Guid itemId)
        => new()
        {
            MovementId = movementId,
            DocumentId = Guid.NewGuid(),
            OccurredAtUtc = new DateTime(2026, 4, 18, 0, 0, 0, DateTimeKind.Utc),
            PeriodMonth = new DateOnly(2026, 4, 1),
            DimensionSetId = Guid.NewGuid(),
            Dimensions = new DimensionBag(
            [
                new DimensionValue(DeterministicGuid.Create($"Dimension|{TradeCodes.Item}"), itemId),
                new DimensionValue(DeterministicGuid.Create($"Dimension|{TradeCodes.Warehouse}"), warehouseId)
            ]),
            Values = new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["qty_delta"] = 1m
            }
        };
}
