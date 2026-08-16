using System.Text.Json;
using FluentAssertions;
using Moq;
using NGB.Contracts.Reporting;
using NGB.Core.Dimensions;
using NGB.Persistence.Documents;
using NGB.ReferenceRegisters.Contracts;
using NGB.Runtime.ReferenceRegisters;
using NGB.Tools.Extensions;
using NGB.Trade.Runtime.Reporting;

namespace NGB.Trade.Runtime.Tests.Reporting;

public sealed class CurrentItemPricesCanonicalReportExecutorFullCoverageTests
{
    private static readonly Guid ItemDimensionId = DeterministicGuid.Create($"Dimension|{TradeCodes.Item}");
    private static readonly Guid PriceTypeDimensionId = DeterministicGuid.Create($"Dimension|{TradeCodes.PriceType}");

    [Fact]
    public async Task ExecuteAsync_CoversAllValueShapesFallbacksFilteringAndPaging()
    {
        var itemA = Guid.CreateVersion7();
        var itemB = Guid.CreateVersion7();
        var priceA = Guid.CreateVersion7();
        var priceB = Guid.CreateVersion7();
        var sourceA = Guid.CreateVersion7();
        var sourceB = Guid.CreateVersion7();
        var snapshots = new[]
        {
            Snapshot(itemA, priceA, new DateOnly(2026, 4, 1), sourceA, "USD", withDisplays: true),
            Snapshot(itemB, priceB, new DateTime(2026, 4, 2), sourceB.ToString("D"), "EUR"),
            Snapshot(itemA, null, new DateTimeOffset(2026, 4, 3, 12, 0, 0, TimeSpan.Zero), null, null,
                recorderId: sourceB),
            Snapshot(null, priceA, "2026-04-04", "invalid-guid", "GBP"),
            Snapshot(null, null, "2026-04-05T12:30:00Z", Guid.Empty, "CAD"),
            Snapshot(itemB, priceA, 42, null, "JPY"),
            Snapshot(itemA, priceB, null, Guid.Empty.ToString("D"), "AUD", unitPrice: null)
        };
        var read = new Mock<IReferenceRegisterReadService>(MockBehavior.Strict);
        read.Setup(x => x.SliceLastAllEnrichedAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<IReadOnlyList<DimensionValue>?>(),
                null, null, 200, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshots);
        var displays = new Mock<IDocumentDisplayReader>(MockBehavior.Strict);
        displays.Setup(x => x.ResolveRefsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> ids, CancellationToken _) =>
                ids.Where(id => id == sourceA).ToDictionary(
                    id => id,
                    id => new DocumentDisplayRef(id, TradeCodes.ItemPriceUpdate, "IPU-1")));
        var sut = new CurrentItemPricesCanonicalReportExecutor(read.Object, displays.Object);
        sut.ReportCode.Should().Be(TradeCodes.CurrentItemPricesReport);
        var definition = new TradeCanonicalReportDefinitionSource().GetDefinitions()
            .Single(item => item.ReportCode == sut.ReportCode);

        var first = await sut.ExecuteAsync(definition, new ReportExecutionRequestDto(Offset: -2, Limit: 1), default);
        var all = await sut.ExecuteAsync(definition, new ReportExecutionRequestDto(DisablePaging: true), default);
        await sut.ExecuteAsync(definition, new ReportExecutionRequestDto(Limit: 0), default);

        first.Total.Should().Be(snapshots.Length);
        first.HasMore.Should().BeTrue();
        all.PrebuiltSheet!.Rows.Should().HaveCount(snapshots.Length);

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
        noSource.Total.Should().Be(1);
    }

    private static ReportFilterValueDto Filter(Guid id) =>
        new(JsonSerializer.SerializeToElement(new[] { id }));

    private static ReferenceRegisterRecordSnapshot Snapshot(
        Guid? itemId,
        Guid? priceTypeId,
        object? effectiveDate,
        object? sourceDocumentId,
        object? currency,
        bool withDisplays = false,
        Guid? recorderId = null,
        decimal? unitPrice = 12.3456m)
    {
        var dimensions = new List<DimensionValue>();
        if (itemId.HasValue) dimensions.Add(new DimensionValue(ItemDimensionId, itemId.Value));
        if (priceTypeId.HasValue) dimensions.Add(new DimensionValue(PriceTypeDimensionId, priceTypeId.Value));
        var displays = new Dictionary<Guid, string>();
        if (withDisplays && itemId.HasValue) displays[ItemDimensionId] = "Item A";
        if (withDisplays && priceTypeId.HasValue) displays[PriceTypeDimensionId] = "Retail";
        var values = new Dictionary<string, object?>
        {
            ["unit_price"] = unitPrice,
            ["effective_date"] = effectiveDate,
            ["source_document_id"] = sourceDocumentId,
            ["currency"] = currency
        };
        return new ReferenceRegisterRecordSnapshot(
            new ReferenceRegisterRecordRead(
                1, Guid.CreateVersion7(), null, null, recorderId, DateTime.UtcNow, false, values),
            new DimensionBag(dimensions),
            displays);
    }
}
