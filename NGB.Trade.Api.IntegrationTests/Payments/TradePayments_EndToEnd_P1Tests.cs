using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.Contracts.Reporting;
using NGB.Tools.Exceptions;
using NGB.Trade.Api.IntegrationTests.Infrastructure;
using NGB.Trade.Runtime;
using Xunit;

namespace NGB.Trade.Api.IntegrationTests.Payments;

[Collection(TradePostgresCollection.Name)]
public sealed class TradePayments_EndToEnd_P1Tests(TradePostgresFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CustomerPayment_And_VendorPayment_Post_ToGeneralJournal_AndUnpostCleanly()
    {
        using var host = TradeHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var setup = scope.ServiceProvider.GetRequiredService<ITradeSetupService>();
        var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();
        var reports = scope.ServiceProvider.GetRequiredService<IReportEngine>();

        var defaults = await setup.EnsureDefaultsAsync(CancellationToken.None);

        var unitOfMeasureId = await GetCatalogIdByDisplayAsync(catalogs, TradeCodes.UnitOfMeasure, "Each");
        var retailPriceTypeId = await GetCatalogIdByDisplayAsync(catalogs, TradeCodes.PriceType, "Retail");

        var warehouse = await catalogs.CreateAsync(
            TradeCodes.Warehouse,
            TradePayloads.Payload(new
            {
                display = "Main Warehouse",
                warehouse_code = "MAIN",
                name = "Main Warehouse",
                address = "100 Harbor Blvd, Miami, FL",
                is_active = true
            }),
            CancellationToken.None);

        var vendor = await catalogs.CreateAsync(
            TradeCodes.Party,
            TradePayloads.Payload(new
            {
                display = "Acme Supply",
                name = "Acme Supply",
                is_customer = false,
                is_vendor = true,
                is_active = true,
                default_currency = "USD"
            }),
            CancellationToken.None);

        var customer = await catalogs.CreateAsync(
            TradeCodes.Party,
            TradePayloads.Payload(new
            {
                display = "Northwind Retail",
                name = "Northwind Retail",
                is_customer = true,
                is_vendor = false,
                is_active = true,
                default_currency = "USD"
            }),
            CancellationToken.None);

        var item = await catalogs.CreateAsync(
            TradeCodes.Item,
            TradePayloads.Payload(new
            {
                display = "Demo Widget",
                name = "Demo Widget",
                sku = "DW-300",
                unit_of_measure_id = unitOfMeasureId,
                default_sales_price_type_id = retailPriceTypeId,
                is_inventory_item = true,
                is_active = true
            }),
            CancellationToken.None);

        var purchaseDraft = await documents.CreateDraftAsync(
            TradeCodes.PurchaseReceipt,
            TradePayloads.Payload(
                new
                {
                    document_date_utc = "2026-04-10",
                    vendor_id = vendor.Id,
                    warehouse_id = warehouse.Id,
                    notes = "Initial stocking"
                },
                TradePayloads.PurchaseReceiptLines(
                    new TradePayloads.PurchaseReceiptLineRow(
                        Ordinal: 1,
                        ItemId: item.Id,
                        Quantity: 10m,
                        UnitCost: 12m,
                        LineAmount: 120m))),
            CancellationToken.None);

        var purchasePosted = await documents.PostAsync(TradeCodes.PurchaseReceipt, purchaseDraft.Id, CancellationToken.None);

        var salesDraft = await documents.CreateDraftAsync(
            TradeCodes.SalesInvoice,
            TradePayloads.Payload(
                new
                {
                    document_date_utc = "2026-04-11",
                    customer_id = customer.Id,
                    warehouse_id = warehouse.Id,
                    price_type_id = retailPriceTypeId,
                    notes = "Customer sale"
                },
                TradePayloads.SalesInvoiceLines(
                    new TradePayloads.SalesInvoiceLineRow(
                        Ordinal: 1,
                        ItemId: item.Id,
                        Quantity: 4m,
                        UnitPrice: 20m,
                        UnitCost: 12m,
                        LineAmount: 80m))),
            CancellationToken.None);

        var salesPosted = await documents.PostAsync(TradeCodes.SalesInvoice, salesDraft.Id, CancellationToken.None);

        var customerPaymentDraft = await documents.CreateDraftAsync(
            TradeCodes.CustomerPayment,
            TradePayloads.Payload(new
            {
                document_date_utc = "2026-04-12",
                customer_id = customer.Id,
                cash_account_id = defaults.CashAccountId,
                sales_invoice_id = salesPosted.Id,
                amount = 80m,
                notes = "Full customer payment"
            }),
            CancellationToken.None);

        customerPaymentDraft.Number.Should().NotBeNullOrWhiteSpace();

        var vendorPaymentDraft = await documents.CreateDraftAsync(
            TradeCodes.VendorPayment,
            TradePayloads.Payload(new
            {
                document_date_utc = "2026-04-12",
                vendor_id = vendor.Id,
                purchase_receipt_id = purchasePosted.Id,
                amount = 120m,
                notes = "Vendor settlement"
            }),
            CancellationToken.None);

        vendorPaymentDraft.Number.Should().NotBeNullOrWhiteSpace();

        var customerPaymentPosted = await documents.PostAsync(TradeCodes.CustomerPayment, customerPaymentDraft.Id, CancellationToken.None);
        customerPaymentPosted.Status.Should().Be(DocumentStatus.Posted);

        var customerPaymentGraph = await documents.GetRelationshipGraphAsync(
            TradeCodes.CustomerPayment,
            customerPaymentPosted.Id,
            depth: 1,
            maxNodes: 20,
            CancellationToken.None);
        customerPaymentGraph.Nodes.Select(x => x.EntityId).Should().BeEquivalentTo([customerPaymentPosted.Id, salesPosted.Id]);
        customerPaymentGraph.Nodes.Should().ContainSingle(x =>
            x.EntityId == customerPaymentPosted.Id
            && x.Amount == 80m);
        customerPaymentGraph.Nodes.Should().ContainSingle(x =>
            x.EntityId == salesPosted.Id
            && x.Amount == 80m);
        customerPaymentGraph.Edges.Should().ContainSingle(x =>
            x.FromNodeId == NodeId(TradeCodes.CustomerPayment, customerPaymentPosted.Id)
            && x.ToNodeId == NodeId(TradeCodes.SalesInvoice, salesPosted.Id)
            && x.RelationshipType == "based_on");

        var vendorPaymentPosted = await documents.PostAsync(TradeCodes.VendorPayment, vendorPaymentDraft.Id, CancellationToken.None);
        vendorPaymentPosted.Status.Should().Be(DocumentStatus.Posted);

        var vendorPaymentGraph = await documents.GetRelationshipGraphAsync(
            TradeCodes.VendorPayment,
            vendorPaymentPosted.Id,
            depth: 1,
            maxNodes: 20,
            CancellationToken.None);
        vendorPaymentGraph.Nodes.Select(x => x.EntityId).Should().BeEquivalentTo([vendorPaymentPosted.Id, purchasePosted.Id]);
        vendorPaymentGraph.Nodes.Should().ContainSingle(x =>
            x.EntityId == vendorPaymentPosted.Id
            && x.Amount == 120m);
        vendorPaymentGraph.Nodes.Should().ContainSingle(x =>
            x.EntityId == purchasePosted.Id
            && x.Amount == 120m);
        vendorPaymentGraph.Edges.Should().ContainSingle(x =>
            x.FromNodeId == NodeId(TradeCodes.VendorPayment, vendorPaymentPosted.Id)
            && x.ToNodeId == NodeId(TradeCodes.PurchaseReceipt, purchasePosted.Id)
            && x.RelationshipType == "based_on");

        var customerPaymentEffects = await documents.GetEffectsAsync(TradeCodes.CustomerPayment, customerPaymentPosted.Id, limit: 20, CancellationToken.None);
        customerPaymentEffects.AccountingEntries.Should().HaveCount(1);
        customerPaymentEffects.OperationalRegisterMovements.Should().BeEmpty();
        customerPaymentEffects.AccountingEntries[0].DebitAccount.Code.Should().Be("1000");
        customerPaymentEffects.AccountingEntries[0].CreditAccount.Code.Should().Be("1100");
        customerPaymentEffects.AccountingEntries[0].Amount.Should().Be(80m);

        var vendorPaymentEffects = await documents.GetEffectsAsync(TradeCodes.VendorPayment, vendorPaymentPosted.Id, limit: 20, CancellationToken.None);
        vendorPaymentEffects.AccountingEntries.Should().HaveCount(1);
        vendorPaymentEffects.OperationalRegisterMovements.Should().BeEmpty();
        vendorPaymentEffects.AccountingEntries[0].DebitAccount.Code.Should().Be("2000");
        vendorPaymentEffects.AccountingEntries[0].CreditAccount.Code.Should().Be("1000");
        vendorPaymentEffects.AccountingEntries[0].Amount.Should().Be(120m);

        var journalResponse = await reports.ExecuteAsync(
            "accounting.general_journal",
            new ReportExecutionRequestDto(
                DisablePaging: true,
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["from_utc"] = "2026-04-01",
                    ["to_utc"] = "2026-04-30"
                }),
            CancellationToken.None);

        journalResponse.Sheet.Rows.Should().HaveCount(5);
        journalResponse.Sheet.Rows.Should().Contain(row =>
            row.Cells[2].Display!.Contains("Operating Cash", StringComparison.Ordinal)
            && row.Cells[4].Display!.Contains("Accounts Receivable", StringComparison.Ordinal)
            && row.Cells[6].Display == "80");
        journalResponse.Sheet.Rows.Should().Contain(row =>
            row.Cells[2].Display!.Contains("Accounts Payable", StringComparison.Ordinal)
            && row.Cells[4].Display!.Contains("Operating Cash", StringComparison.Ordinal)
            && row.Cells[6].Display == "120");

        var customerPaymentAfterUnpost = await documents.UnpostAsync(TradeCodes.CustomerPayment, customerPaymentPosted.Id, CancellationToken.None);
        customerPaymentAfterUnpost.Status.Should().Be(DocumentStatus.Draft);

        var customerPaymentEffectsAfterUnpost = await documents.GetEffectsAsync(TradeCodes.CustomerPayment, customerPaymentPosted.Id, limit: 20, CancellationToken.None);
        customerPaymentEffectsAfterUnpost.AccountingEntries.Should().BeEmpty();
        customerPaymentEffectsAfterUnpost.OperationalRegisterMovements.Should().BeEmpty();
    }

    [Fact]
    public async Task CustomerPayment_Rejects_Unposted_SalesInvoice_Reference()
    {
        using var host = TradeHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var setup = scope.ServiceProvider.GetRequiredService<ITradeSetupService>();
        var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

        await setup.EnsureDefaultsAsync(CancellationToken.None);

        var unitOfMeasureId = await GetCatalogIdByDisplayAsync(catalogs, TradeCodes.UnitOfMeasure, "Each");
        var retailPriceTypeId = await GetCatalogIdByDisplayAsync(catalogs, TradeCodes.PriceType, "Retail");

        var warehouse = await catalogs.CreateAsync(
            TradeCodes.Warehouse,
            TradePayloads.Payload(new
            {
                display = "Main Warehouse",
                warehouse_code = "MAIN",
                name = "Main Warehouse",
                address = "100 Harbor Blvd, Miami, FL",
                is_active = true
            }),
            CancellationToken.None);

        var customer = await catalogs.CreateAsync(
            TradeCodes.Party,
            TradePayloads.Payload(new
            {
                display = "Northwind Retail",
                name = "Northwind Retail",
                is_customer = true,
                is_vendor = false,
                is_active = true,
                default_currency = "USD"
            }),
            CancellationToken.None);

        var item = await catalogs.CreateAsync(
            TradeCodes.Item,
            TradePayloads.Payload(new
            {
                display = "Demo Widget",
                name = "Demo Widget",
                sku = "DW-301",
                unit_of_measure_id = unitOfMeasureId,
                default_sales_price_type_id = retailPriceTypeId,
                is_inventory_item = true,
                is_active = true
            }),
            CancellationToken.None);

        var salesDraft = await documents.CreateDraftAsync(
            TradeCodes.SalesInvoice,
            TradePayloads.Payload(
                new
                {
                    document_date_utc = "2026-04-11",
                    customer_id = customer.Id,
                    warehouse_id = warehouse.Id,
                    price_type_id = retailPriceTypeId,
                    notes = "Customer sale"
                },
                TradePayloads.SalesInvoiceLines(
                    new TradePayloads.SalesInvoiceLineRow(
                        Ordinal: 1,
                        ItemId: item.Id,
                        Quantity: 2m,
                        UnitPrice: 20m,
                        UnitCost: 12m,
                        LineAmount: 40m))),
            CancellationToken.None);

        var paymentDraft = await documents.CreateDraftAsync(
            TradeCodes.CustomerPayment,
            TradePayloads.Payload(new
            {
                document_date_utc = "2026-04-12",
                customer_id = customer.Id,
                sales_invoice_id = salesDraft.Id,
                amount = 40m,
                notes = "Attempted early payment"
            }),
            CancellationToken.None);

        Func<Task> act = () => documents.PostAsync(TradeCodes.CustomerPayment, paymentDraft.Id, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("sales_invoice_id");
        ex.Which.Reason.Should().Be("Referenced Sales Invoice must be posted.");
    }

    private static async Task<Guid> GetCatalogIdByDisplayAsync(
        ICatalogService catalogs,
        string catalogType,
        string display)
    {
        var page = await catalogs.GetPageAsync(
            catalogType,
            new PageRequestDto(Offset: 0, Limit: 50, Search: null),
            CancellationToken.None);

        return page.Items.Single(x => string.Equals(x.Display, display, StringComparison.OrdinalIgnoreCase)).Id;
    }

    private static string NodeId(string typeCode, Guid id) => $"doc:{typeCode}:{id}";
}
