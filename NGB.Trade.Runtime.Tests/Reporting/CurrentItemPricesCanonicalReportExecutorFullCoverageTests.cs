using System.Text.Json;
using FluentAssertions;
using Moq;
using NGB.Contracts.Reporting;
using NGB.Persistence.Documents;
using NGB.Trade.Reporting;
using NGB.Trade.Runtime.Reporting;

namespace NGB.Trade.Runtime.Tests.Reporting;

public sealed class CurrentItemPricesCanonicalReportExecutorFullCoverageTests
{
    [Fact]
    public async Task ExecuteAsync_UsesPersistencePagingFilteringAndBatchedDocumentDisplays()
    {
        var itemA = Guid.CreateVersion7();
        var itemB = Guid.CreateVersion7();
        var priceA = Guid.CreateVersion7();
        var priceB = Guid.CreateVersion7();
        var sourceA = Guid.CreateVersion7();
        var sourceB = Guid.CreateVersion7();
        var prices = new[]
        {
            new TradeCurrentItemPriceRow(itemA, "Item A", priceA, "Retail", "USD", 12.3456m, new DateOnly(2026, 4, 1), sourceA),
            new TradeCurrentItemPriceRow(itemB, "Item B", priceB, "Wholesale", "EUR", 20m, new DateOnly(2026, 4, 2), sourceB),
            new TradeCurrentItemPriceRow(itemA, "Item A", priceB, "Wholesale", "AUD", 0m, null, null)
        };
        var read = new CurrentItemPriceReaderStub(prices);
        var displays = new Mock<IDocumentDisplayReader>(MockBehavior.Strict);
        displays.Setup(x => x.ResolveRefsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> ids, CancellationToken _) =>
                ids.Where(id => id == sourceA).ToDictionary(
                    id => id,
                    id => new DocumentDisplayRef(id, TradeCodes.ItemPriceUpdate, "IPU-1")));
        var sut = new CurrentItemPricesCanonicalReportExecutor(read, displays.Object);
        sut.ReportCode.Should().Be(TradeCodes.CurrentItemPricesReport);
        var definition = new TradeCanonicalReportDefinitionSource().GetDefinitions()
            .Single(item => item.ReportCode == sut.ReportCode);

        var first = await sut.ExecuteAsync(definition, new ReportExecutionRequestDto(Offset: -2, Limit: 1), default);
        var cursorPage = await sut.ExecuteAsync(
            definition,
            new ReportExecutionRequestDto(Cursor: first.NextCursor, Limit: 1),
            default);
        var all = await sut.ExecuteAsync(definition, new ReportExecutionRequestDto(DisablePaging: true), default);
        await sut.ExecuteAsync(definition, new ReportExecutionRequestDto(Limit: 0), default);

        first.Total.Should().Be(prices.Length);
        first.HasMore.Should().BeTrue();
        first.NextCursor.Should().NotBeNullOrWhiteSpace();
        cursorPage.Offset.Should().Be(1);
        cursorPage.Total.Should().Be(prices.Length);
        all.PrebuiltSheet!.Rows.Should().HaveCount(prices.Length);

        var exact = await sut.ExecuteAsync(definition, new ReportExecutionRequestDto(
            Filters: new Dictionary<string, ReportFilterValueDto>
            {
                ["item_id"] = Filter(itemA),
                ["price_type_id"] = Filter(priceA)
            }, DisablePaging: true), default);
        exact.Total.Should().Be(1);

        var noSource = await sut.ExecuteAsync(definition, new ReportExecutionRequestDto(
            Filters: new Dictionary<string, ReportFilterValueDto>
            {
                ["item_id"] = Filter(itemB),
                ["price_type_id"] = Filter(priceA)
            }, DisablePaging: true), default);
        noSource.Total.Should().Be(0);
        displays.Verify(x => x.ResolveRefsAsync(
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.Distinct().Count() == ids.Count),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    private static ReportFilterValueDto Filter(Guid id) =>
        new(JsonSerializer.SerializeToElement(new[] { id }));

    private sealed class CurrentItemPriceReaderStub(IReadOnlyList<TradeCurrentItemPriceRow> rows)
        : ITradeCurrentItemPriceReader
    {
        public Task<TradeCurrentItemPricePage> GetPageAsync(
            DateTime asOfUtc,
            IReadOnlyList<Guid>? itemIds,
            IReadOnlyList<Guid>? priceTypeIds,
            int offset,
            int limit,
            CancellationToken ct = default)
        {
            IEnumerable<TradeCurrentItemPriceRow> filtered = rows;
            if (itemIds is { Count: > 0 })
                filtered = filtered.Where(row => itemIds.Contains(row.ItemId));
            if (priceTypeIds is { Count: > 0 })
                filtered = filtered.Where(row => priceTypeIds.Contains(row.PriceTypeId));
            var materialized = filtered
                .OrderBy(static row => row.ItemDisplay)
                .ThenBy(static row => row.PriceTypeDisplay)
                .ThenBy(static row => row.Currency)
                .ToArray();
            return Task.FromResult(new TradeCurrentItemPricePage(
                materialized.Skip(offset).Take(limit).ToArray(),
                materialized.Length));
        }
    }
}
