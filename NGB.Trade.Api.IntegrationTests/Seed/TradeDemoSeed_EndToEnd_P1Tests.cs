using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Reporting;
using NGB.Trade.Api.IntegrationTests.Infrastructure;
using NGB.Trade.Runtime;
using Xunit;

namespace NGB.Trade.Api.IntegrationTests.Seed;

[Collection(TradePostgresCollection.Name)]
public sealed class TradeDemoSeed_EndToEnd_P1Tests(TradePostgresFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task EnsureDemo_Is_Idempotent_And_Populates_Dashboard_Data()
    {
        using var host = TradeHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var demoSeed = scope.ServiceProvider.GetRequiredService<ITradeDemoSeedService>();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();
        var reports = scope.ServiceProvider.GetRequiredService<IReportEngine>();

        var first = await demoSeed.EnsureDemoAsync(CancellationToken.None);
        first.WarehousesEnsured.Should().Be(2);
        first.PartnersEnsured.Should().Be(4);
        first.ItemsEnsured.Should().Be(3);
        first.DocumentsCreated.Should().Be(11);
        first.SeededOperationalData.Should().BeTrue();

        var second = await demoSeed.EnsureDemoAsync(CancellationToken.None);
        second.DocumentsCreated.Should().Be(0);
        second.SeededOperationalData.Should().BeFalse();

        (await GetDocumentCountAsync(documents, TradeCodes.ItemPriceUpdate)).Should().Be(1);
        (await GetDocumentCountAsync(documents, TradeCodes.PurchaseReceipt)).Should().Be(2);
        (await GetDocumentCountAsync(documents, TradeCodes.SalesInvoice)).Should().Be(2);
        (await GetDocumentCountAsync(documents, TradeCodes.CustomerPayment)).Should().Be(1);
        (await GetDocumentCountAsync(documents, TradeCodes.VendorPayment)).Should().Be(1);
        (await GetDocumentCountAsync(documents, TradeCodes.InventoryTransfer)).Should().Be(1);
        (await GetDocumentCountAsync(documents, TradeCodes.InventoryAdjustment)).Should().Be(1);
        (await GetDocumentCountAsync(documents, TradeCodes.CustomerReturn)).Should().Be(1);
        (await GetDocumentCountAsync(documents, TradeCodes.VendorReturn)).Should().Be(1);

        var dashboard = await reports.ExecuteAsync(
            TradeCodes.DashboardOverviewReport,
            new ReportExecutionRequestDto(
                DisablePaging: true,
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["as_of_utc"] = first.AsOfUtc.ToString("yyyy-MM-dd")
                }),
            CancellationToken.None);

        dashboard.Sheet.Rows.Should().Contain(row =>
            row.Cells.Count >= 5
            && row.Cells[1].Display == "Sales This Month"
            && row.Cells[2].Display == "240");
        dashboard.Sheet.Rows.Should().Contain(row =>
            row.Cells.Count >= 5
            && row.Cells[1].Display == "Purchases This Month"
            && row.Cells[2].Display == "488");
        dashboard.Sheet.Rows.Should().Contain(row =>
            row.Cells.Count >= 5
            && row.Cells[1].Display == "Inventory On Hand"
            && row.Cells[2].Display == "157");
        dashboard.Sheet.Rows.Should().Contain(row =>
            row.Cells.Count >= 5
            && row.Cells[1].Display == "Gross Margin"
            && row.Cells[2].Display == "136");
        dashboard.Sheet.Rows.Should().Contain(row =>
            row.Cells.Count >= 5
            && row.Cells[0].Display == "Top Item"
            && row.Cells[1].Display == "Bravo Gadget"
            && row.Cells[2].Display == "120"
            && row.Cells[3].Display == "15");

        var salesByItem = await reports.ExecuteAsync(
            TradeCodes.SalesByItemReport,
            new ReportExecutionRequestDto(
                DisablePaging: true,
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["from_utc"] = new DateOnly(first.AsOfUtc.Year, first.AsOfUtc.Month, 1).ToString("yyyy-MM-dd"),
                    ["to_utc"] = first.AsOfUtc.ToString("yyyy-MM-dd")
                }),
            CancellationToken.None);

        salesByItem.Total.Should().Be(3);
    }

    private static async Task<int> GetDocumentCountAsync(
        IDocumentService documents,
        string documentType)
    {
        var page = await documents.GetPageAsync(
            documentType,
            new PageRequestDto(Offset: 0, Limit: 1, Search: null),
            CancellationToken.None);

        return page.Total.GetValueOrDefault(page.Items.Count);
    }
}
