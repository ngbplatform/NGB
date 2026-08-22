using FluentAssertions;
using Moq;
using NGB.Api.Models;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Metadata.Catalogs.Storage;
using NGB.Metadata.Documents.Hybrid;
using NGB.Metadata.Documents.Storage;
using NGB.Trade.Api.Services;
using Xunit;

namespace NGB.Trade.Api.IntegrationTests.Services;

public sealed class TradeMainMenuContributorFullCoverageTests
{
    [Fact]
    public async Task ContributeAsync_AllResourcesAndLinks_ReturnsCompleteOrderedMenu()
    {
        var catalogs = CatalogRegistry(
            TradeCodes.Item,
            TradeCodes.Warehouse,
            TradeCodes.UnitOfMeasure,
            TradeCodes.Party,
            TradeCodes.PriceType,
            TradeCodes.AccountingPolicy,
            TradeCodes.PaymentTerms,
            TradeCodes.InventoryAdjustmentReason);
        var documents = DocumentRegistry(
            TradeCodes.InventoryTransfer,
            TradeCodes.InventoryAdjustment,
            TradeCodes.PurchaseReceipt,
            TradeCodes.VendorPayment,
            TradeCodes.VendorReturn,
            TradeCodes.SalesInvoice,
            TradeCodes.CustomerPayment,
            TradeCodes.CustomerReturn,
            TradeCodes.ItemPriceUpdate);
        var sut = new TradeMainMenuContributor(
            catalogs.Object,
            documents.Object,
            new ExternalLinksSettings("https://health", "https://jobs"));

        var result = await sut.ContributeAsync(CancellationToken.None);

        result.Select(x => x.Label).Should().Equal(
            "Dashboard", "Inventory", "Purchasing", "Sales", "Pricing", "Setup & Controls");
        result.SelectMany(x => x.Items).Should().Contain(x => x.Code == TradeCodes.Watchdog && x.Kind == "external");
        result.SelectMany(x => x.Items).Should().Contain(x => x.Code == TradeCodes.BackgroundJobs && x.Route == "https://jobs");
        result.Should().OnlyContain(x => x.Items.Select(item => item.Ordinal).SequenceEqual(x.Items.Select(item => item.Ordinal).Order()));
    }

    [Fact]
    public async Task ContributeAsync_MissingOptionalResourcesAndLinks_ReturnsOnlyPages()
    {
        var sut = new TradeMainMenuContributor(
            CatalogRegistry().Object,
            DocumentRegistry().Object,
            new ExternalLinksSettings(" ", ""));

        var result = await sut.ContributeAsync(CancellationToken.None);

        result.Should().HaveCount(6);
        result[0].Items.Should().ContainSingle();
        result.Skip(1).SelectMany(x => x.Items).Should().OnlyContain(x => x.Kind == "page");
    }

    private static Mock<ICatalogTypeRegistry> CatalogRegistry(params string[] codes)
    {
        var registry = new Mock<ICatalogTypeRegistry>();
        registry.Setup(x => x.All()).Returns(codes
            .Select(code => new CatalogTypeMetadata(
                code.ToUpperInvariant(),
                code,
                [],
                new CatalogPresentationMetadata("table", "display"),
                new CatalogMetadataVersion(1, "hash")))
            .ToArray());
        return registry;
    }

    private static Mock<IDocumentTypeRegistry> DocumentRegistry(params string[] codes)
    {
        var registry = new Mock<IDocumentTypeRegistry>();
        registry.Setup(x => x.GetAll()).Returns(codes
            .Select(code => new DocumentTypeMetadata(code.ToUpperInvariant(), []))
            .ToArray());
        return registry;
    }
}
