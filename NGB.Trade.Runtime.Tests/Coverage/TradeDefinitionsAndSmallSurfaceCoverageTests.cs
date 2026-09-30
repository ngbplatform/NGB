using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Definitions;
using NGB.Definitions.Documents.Numbering;
using NGB.Definitions.Documents.Posting;
using NGB.Trade.Definitions;
using NGB.Trade.DependencyInjection;
using NGB.Trade.Documents.Numbering;
using NGB.Trade.Reporting;
using NGB.Trade.Runtime.DependencyInjection;
using NGB.Trade.Runtime.Posting;
using NGB.Trade.Runtime.Reporting;
using NGB.Trade.Runtime.Reporting.Datasets;

namespace NGB.Trade.Runtime.Tests.Coverage;

public sealed class TradeDefinitionsAndSmallSurfaceCoverageTests
{
    [Fact]
    public void DefinitionContributors_BuildEveryCatalogDocumentAndPostingBinding()
    {
        var builder = new DefinitionsBuilder();

        new TradeDefinitionsContributor().Contribute(builder);
        new TradePostingDefinitionsContributor().Contribute(builder);
        var registry = builder.Build();

        foreach (var code in new[]
                 {
                     TradeCodes.Party,
                     TradeCodes.Item,
                     TradeCodes.Warehouse,
                     TradeCodes.UnitOfMeasure,
                     TradeCodes.PaymentTerms,
                     TradeCodes.InventoryAdjustmentReason,
                     TradeCodes.PriceType,
                     TradeCodes.AccountingPolicy
                 })
            registry.GetCatalog(code).Metadata.Tables.Should().NotBeEmpty();

        foreach (var code in new[]
                 {
                     TradeCodes.PurchaseReceipt,
                     TradeCodes.SalesInvoice,
                     TradeCodes.CustomerPayment,
                     TradeCodes.VendorPayment,
                     TradeCodes.InventoryTransfer,
                     TradeCodes.InventoryAdjustment,
                     TradeCodes.CustomerReturn,
                     TradeCodes.VendorReturn,
                     TradeCodes.ItemPriceUpdate
                 })
            registry.GetDocument(code).Metadata.Tables.Should().NotBeEmpty();

        registry.GetDocument(TradeCodes.SalesInvoice).PostingHandlerType
            .Should().Be(typeof(SalesInvoicePostingHandler));
        registry.GetDocument(TradeCodes.ItemPriceUpdate).ReferenceRegisterPostingHandlerType
            .Should().Be(typeof(ItemPriceUpdateReferenceRegisterPostingHandler));
    }

    [Fact]
    public void ModuleRegistrations_AreIdempotentAndReturnSameCollection()
    {
        var services = new ServiceCollection();

        services.AddTradeModule().Should().BeSameAs(services);
        services.AddTradeModule().Should().BeSameAs(services);
        services.AddTradeRuntimeModule().Should().BeSameAs(services);
        services.AddTradeRuntimeModule().Should().BeSameAs(services);

        services.Count(descriptor => descriptor.ServiceType == typeof(TrdSalesInvoiceNumberingPolicy)).Should().Be(1);
        services.Count(descriptor => descriptor.ServiceType == typeof(IDocumentNumberingPolicy)
                                     && descriptor.ImplementationType == typeof(TrdSalesInvoiceNumberingPolicy)).Should().Be(1);
        services.Count(descriptor => descriptor.ServiceType == typeof(IDocumentPostingHandler)
                                     && descriptor.ImplementationType == typeof(SalesInvoicePostingHandler)).Should().Be(1);
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IReportDefinitionSource)
                                                && descriptor.ImplementationType == typeof(TradeCanonicalReportDefinitionSource));
    }

    [Fact]
    public void ReportDefinitionAndDatasetSources_ReturnEveryTradeReport()
    {
        new TradeCanonicalReportDefinitionSource().GetDefinitions()
            .Select(definition => definition.ReportCode)
            .Should().Equal(
                TradeCodes.DashboardOverviewReport,
                TradeCodes.InventoryBalancesReport,
                TradeCodes.InventoryMovementsReport,
                TradeCodes.SalesByItemReport,
                TradeCodes.SalesByCustomerReport,
                TradeCodes.PurchasesByVendorReport,
                TradeCodes.CurrentItemPricesReport);

        new TradeOperationalReportsDatasetSource().GetDatasets()
            .Select(dataset => dataset.DatasetCode)
            .Should().Equal(TradeCodes.InventoryBalancesReport, TradeCodes.InventoryMovementsReport);
    }

    [Theory]
    [InlineData(0, 10, 0)]
    [InlineData(100, 40, 60)]
    public void SalesByItem_MarginPropertiesCoverZeroAndNonZeroNetSales(
        int netSales, int netCogs, int expectedPercent)
    {
        var row = new SalesByItemSummaryRow(
            Guid.CreateVersion7(), "Item", 1m, 1m, 0m, 0m, netSales, netCogs);

        row.GrossMargin.Should().Be(netSales - netCogs);
        row.MarginPercent.Should().Be(expectedPercent);
    }

    [Theory]
    [InlineData(0, 10, 0)]
    [InlineData(100, 40, 60)]
    public void SalesByCustomer_MarginPropertiesCoverZeroAndNonZeroNetSales(
        int netSales, int netCogs, int expectedPercent)
    {
        var row = new SalesByCustomerSummaryRow(
            Guid.CreateVersion7(), "Customer", 1, 0, 1m, 0m, netSales, netCogs);

        row.GrossMargin.Should().Be(netSales - netCogs);
        row.MarginPercent.Should().Be(expectedPercent);
    }
}
