using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.Contracts.Reporting;
using NGB.Trade.Api.IntegrationTests.Infrastructure;
using NGB.Trade.Runtime;
using Xunit;

namespace NGB.Trade.Api.IntegrationTests.Pricing;

[Collection(TradePostgresCollection.Name)]
public sealed class TradePricing_EndToEnd_P0Tests(TradePostgresFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task EnsureDefaults_IsIdempotent_AndExposesTradeReportDefinition()
    {
        using var host = TradeHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var setup = scope.ServiceProvider.GetRequiredService<ITradeSetupService>();
        var definitions = scope.ServiceProvider.GetRequiredService<IReportDefinitionProvider>();

        var first = await setup.EnsureDefaultsAsync(CancellationToken.None);
        var second = await setup.EnsureDefaultsAsync(CancellationToken.None);

        first.CreatedCashAccount.Should().BeTrue();
        first.CreatedItemPricesReferenceRegister.Should().BeTrue();
        first.CreatedAccountingPolicy.Should().BeTrue();

        second.CreatedCashAccount.Should().BeFalse();
        second.CreatedItemPricesReferenceRegister.Should().BeFalse();
        second.CreatedAccountingPolicy.Should().BeFalse();

        second.CashAccountId.Should().Be(first.CashAccountId);
        second.ItemPricesReferenceRegisterId.Should().Be(first.ItemPricesReferenceRegisterId);
        second.AccountingPolicyCatalogId.Should().Be(first.AccountingPolicyCatalogId);

        var definition = await definitions.GetDefinitionAsync(TradeCodes.CurrentItemPricesReport, CancellationToken.None);
        definition.Name.Should().Be("Current Item Prices");
        definition.Filters.Should().HaveCount(2);
        definition.Filters.Should().OnlyContain(filter => filter.IsMulti);
        definition.Filters![0].Lookup.Should().BeOfType<CatalogLookupSourceDto>();
        ((CatalogLookupSourceDto)definition.Filters[0].Lookup!).CatalogType.Should().Be(TradeCodes.Item);
        definition.Filters[1].Lookup.Should().BeOfType<CatalogLookupSourceDto>();
        ((CatalogLookupSourceDto)definition.Filters[1].Lookup!).CatalogType.Should().Be(TradeCodes.PriceType);
    }

    [Fact]
    public async Task ItemPriceUpdate_Post_Unpost_UpdatesCurrentPricesReport()
    {
        using var host = TradeHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var setup = scope.ServiceProvider.GetRequiredService<ITradeSetupService>();
        var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();
        var definitions = scope.ServiceProvider.GetRequiredService<IReportDefinitionProvider>();
        var reports = scope.ServiceProvider.GetRequiredService<IReportEngine>();

        await setup.EnsureDefaultsAsync(CancellationToken.None);

        var unitOfMeasureId = await GetCatalogIdByDisplayAsync(catalogs, TradeCodes.UnitOfMeasure, "Each");
        var retailPriceTypeId = await GetCatalogIdByDisplayAsync(catalogs, TradeCodes.PriceType, "Retail");

        var item = await catalogs.CreateAsync(
            TradeCodes.Item,
            TradePayloads.Payload(new
            {
                display = "Demo Widget",
                name = "Demo Widget",
                sku = "DW-100",
                unit_of_measure_id = unitOfMeasureId,
                default_sales_price_type_id = retailPriceTypeId,
                is_inventory_item = true,
                is_active = true
            }),
            CancellationToken.None);

        var draft = await documents.CreateDraftAsync(
            TradeCodes.ItemPriceUpdate,
            TradePayloads.Payload(
                new
                {
                    effective_date = "2026-04-15",
                    notes = "Spring pricing refresh"
                },
                TradePayloads.ItemPriceUpdateLines(
                    new TradePayloads.ItemPriceUpdateLineRow(
                        Ordinal: 1,
                        ItemId: item.Id,
                        PriceTypeId: retailPriceTypeId,
                        Currency: "usd",
                        UnitPrice: 19.95m))),
            CancellationToken.None);

        draft.Number.Should().NotBeNullOrWhiteSpace();

        var posted = await documents.PostAsync(TradeCodes.ItemPriceUpdate, draft.Id, CancellationToken.None);
        posted.Status.Should().Be(DocumentStatus.Posted);

        var definition = await definitions.GetDefinitionAsync(TradeCodes.CurrentItemPricesReport, CancellationToken.None);
        var response = await reports.ExecuteAsync(
            TradeCodes.CurrentItemPricesReport,
            new ReportExecutionRequestDto(DisablePaging: true),
            CancellationToken.None);

        response.Total.Should().Be(1);
        response.Sheet.Meta!.Title.Should().Be(definition.Name);
        response.Sheet.Rows.Should().HaveCount(1);

        var row = response.Sheet.Rows.Single();
        row.Cells[0].Display.Should().Be("Demo Widget");
        row.Cells[0].Action!.CatalogType.Should().Be(TradeCodes.Item);
        row.Cells[0].Action!.CatalogId.Should().Be(item.Id);
        row.Cells[1].Display.Should().Be("Retail");
        row.Cells[1].Action!.CatalogType.Should().Be(TradeCodes.PriceType);
        row.Cells[1].Action!.CatalogId.Should().Be(retailPriceTypeId);
        row.Cells[2].Display.Should().Be("USD");
        row.Cells[3].Display.Should().Be("19.95");
        row.Cells[4].Display.Should().Be("2026-04-15");
        row.Cells[5].Display.Should().Be(posted.Display);
        var action = row.Cells[5].Action;
        action.Should().NotBeNull();
        action!.DocumentType.Should().Be(TradeCodes.ItemPriceUpdate);
        action.DocumentId.Should().Be(posted.Id);

        var afterUnpost = await documents.UnpostAsync(TradeCodes.ItemPriceUpdate, posted.Id, CancellationToken.None);
        afterUnpost.Status.Should().Be(DocumentStatus.Draft);

        var afterUnpostResponse = await reports.ExecuteAsync(
            TradeCodes.CurrentItemPricesReport,
            new ReportExecutionRequestDto(DisablePaging: true),
            CancellationToken.None);

        afterUnpostResponse.Total.Should().Be(0);
        afterUnpostResponse.Sheet.Rows.Should().BeEmpty();
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
}
