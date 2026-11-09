using FluentAssertions.Execution;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.Contracts.Reporting;
using NGB.Core.Dimensions;
using NGB.Runtime.ReferenceRegisters;
using NGB.Tools.Extensions;
using NGB.Trade.Api.IntegrationTests.Infrastructure;
using NGB.Trade.Api.IntegrationTests.Support;
using NGB.Trade.Pricing;
using NGB.Trade.Reporting;
using NGB.Trade.Runtime;
using Xunit.Abstractions;
using Xunit;

namespace NGB.Trade.Api.IntegrationTests.Pricing;

[Collection(TradePostgresCollection.Name)]
public sealed class TradeReferenceRegisterConsistencyTests(
    TradePostgresFixture fixture,
    ITestOutputHelper output) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData(-10, 10, 100)]
    [InlineData(-10, -20, 100)]
    [InlineData(-10, -5, 120)]
    [InlineData(-10, -10, 120)]
    public async Task CurrentPriceReport_MustAgreeWithEffectivePriceLookup(
        int firstEffectiveDay,
        int secondEffectiveDay,
        int expectedPrice)
    {
        using var host = TradeHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var key = await CreateItemAsync(services);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await PostPriceAsync(services, key, today.AddDays(firstEffectiveDay), 100m);
        await PostPriceAsync(services, key, today.AddDays(secondEffectiveDay), 120m);

        var (reportPrice, lookupPrice) = await ReadPricesAsync(services, key, today);
        using var assertions = new AssertionScope();
        lookupPrice.Should().Be(expectedPrice);
        reportPrice.Should().Be(expectedPrice,
            "a current price report and the price offered to a sales document on the same date must agree");
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(10, true)]
    [InlineData(0, false)]
    [InlineData(10, false)]
    public async Task UnpostOneOfTwoPriceDocuments_MustPreserveTheOtherPostedPrice(
        int secondEffectiveDayOffset,
        bool cancelOlderDocument)
    {
        using var host = TradeHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var key = await CreateItemAsync(services);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var firstDate = today.AddDays(-20);
        var first = await PostPriceAsync(services, key, firstDate, 100m);
        var second = await PostPriceAsync(services, key, firstDate.AddDays(secondEffectiveDayOffset), 120m);

        var before = await ReadPricesAsync(services, key, today);
        before.ReportPrice.Should().Be(120m);
        before.LookupPrice.Should().Be(120m);

        var documents = services.GetRequiredService<IDocumentService>();
        var cancelled = cancelOlderDocument ? first : second;
        var retained = cancelOlderDocument ? second : first;
        var expectedPrice = cancelOlderDocument ? 120m : 100m;
        await documents.UnpostAsync(TradeCodes.ItemPriceUpdate, cancelled, CancellationToken.None);
        await documents.UnpostAsync(TradeCodes.ItemPriceUpdate, cancelled, CancellationToken.None);

        var retainedDocument = await documents.GetByIdAsync(TradeCodes.ItemPriceUpdate, retained, CancellationToken.None);
        retainedDocument.Status.Should().Be(DocumentStatus.Posted);
        var cancelledDocument = await documents.GetByIdAsync(TradeCodes.ItemPriceUpdate, cancelled, CancellationToken.None);
        cancelledDocument.Status.Should().Be(DocumentStatus.Draft);

        var (reportPrice, lookupPrice) = await ReadPricesAsync(services, key, today);
        using var assertions = new AssertionScope();
        reportPrice.Should().Be(expectedPrice, "the other document remains posted for the same item and price type");
        lookupPrice.Should().Be(expectedPrice, "cancelling one document must not cancel the other document's price");
    }

    [Fact]
    public async Task RepostLatestPriceDocument_IsIdempotent_AndKeepsReportAndLookupConsistent()
    {
        using var host = TradeHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var key = await CreateItemAsync(services);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await PostPriceAsync(services, key, today.AddDays(-10), 100m);
        var latest = await PostPriceAsync(services, key, today.AddDays(-5), 120m);
        var documents = services.GetRequiredService<IDocumentService>();

        await documents.RepostAsync(TradeCodes.ItemPriceUpdate, latest, CancellationToken.None);
        var firstRepost = await ReadPricesAsync(services, key, today);
        await documents.RepostAsync(TradeCodes.ItemPriceUpdate, latest, CancellationToken.None);
        var repeatedRepost = await ReadPricesAsync(services, key, today);

        firstRepost.ReportPrice.Should().Be(120m);
        firstRepost.LookupPrice.Should().Be(120m);
        repeatedRepost.Should().Be(firstRepost);
    }

    [Fact]
    public async Task ExistingPriceHistory_IsReadCorrectlyWithoutMigrationOrRewritingRows()
    {
        using var host = TradeHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var key = await CreateItemAsync(services);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await PostPriceAsync(services, key, today.AddDays(-10), 100m);
        await PostPriceAsync(services, key, today.AddDays(10), 120m);
        var setup = await services.GetRequiredService<ITradeSetupService>().EnsureDefaultsAsync(CancellationToken.None);
        var dimensions = PriceDimensions(key);
        var reads = services.GetRequiredService<IReferenceRegisterReadService>();
        var historyBefore = await reads.GetKeyHistoryAsync(
            setup.ItemPricesReferenceRegisterId, dimensions, DateTime.UtcNow.AddMinutes(1), includeDeleted: true);

        var current = await ReadPricesAsync(services, key, today);
        current.ReportPrice.Should().Be(100m);
        current.LookupPrice.Should().Be(100m);

        var report = await services.GetRequiredService<IReportEngine>().ExecuteAsync(
            TradeCodes.CurrentItemPricesReport, new ReportExecutionRequestDto(DisablePaging: true), CancellationToken.None);
        report.Sheet.Rows.Single().Cells[3].Value!.Value.GetDecimal().Should().Be(100m);
        var historyAfter = await reads.GetKeyHistoryAsync(
            setup.ItemPricesReferenceRegisterId, dimensions, DateTime.UtcNow.AddMinutes(1), includeDeleted: true);
        historyAfter.Should().BeEquivalentTo(historyBefore);
    }

    [Fact]
    public async Task HistoricalReportBeforeUnpost_PreservesPreviouslyKnownPrice()
    {
        using var host = TradeHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var key = await CreateItemAsync(services);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await PostPriceAsync(services, key, today.AddDays(-20), 100m);
        var latest = await PostPriceAsync(services, key, today.AddDays(-10), 120m);
        var setup = await services.GetRequiredService<ITradeSetupService>().EnsureDefaultsAsync(CancellationToken.None);
        var recorded = await services.GetRequiredService<IReferenceRegisterReadService>().SliceLastAsync(
            setup.ItemPricesReferenceRegisterId, PriceDimensions(key), DateTime.UtcNow.AddMinutes(1));
        await services.GetRequiredService<IDocumentService>().UnpostAsync(TradeCodes.ItemPriceUpdate, latest, CancellationToken.None);

        var historical = await services.GetRequiredService<ITradeCurrentItemPriceReader>().GetPageAsync(
            recorded!.RecordedAtUtc, [key.ItemId], [key.PriceTypeId], 0, 20);
        historical.Rows.Single().UnitPrice.Should().Be(120m);
        var current = await ReadPricesAsync(services, key, today);
        current.ReportPrice.Should().Be(100m);
        current.LookupPrice.Should().Be(100m);
    }

    [Fact]
    public async Task UnpostAllPriceSources_DoesNotResurrectTheirOldVersions()
    {
        using var host = TradeHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var key = await CreateItemAsync(services);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var first = await PostPriceAsync(services, key, today.AddDays(-20), 100m);
        var second = await PostPriceAsync(services, key, today.AddDays(-10), 120m);
        var documents = services.GetRequiredService<IDocumentService>();
        await documents.UnpostAsync(TradeCodes.ItemPriceUpdate, first, CancellationToken.None);
        await documents.UnpostAsync(TradeCodes.ItemPriceUpdate, second, CancellationToken.None);
        var current = await ReadPricesAsync(services, key, today);
        current.ReportPrice.Should().BeNull();
        current.LookupPrice.Should().BeNull();
    }

    [Theory]
    [InlineData(10, 100)]
    [InlineData(-5, 130)]
    [InlineData(-30, 100)]
    public async Task EditingAndRepostingPriceDocument_UsesOnlyItsReplacementVersion(
        int replacementOffset,
        int expectedCurrentPrice)
    {
        using var host = TradeHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var key = await CreateItemAsync(services);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await PostPriceAsync(services, key, today.AddDays(-20), 100m);
        var edited = await PostPriceAsync(services, key, today.AddDays(-10), 120m);
        var documents = services.GetRequiredService<IDocumentService>();
        await documents.UnpostAsync(TradeCodes.ItemPriceUpdate, edited, CancellationToken.None);
        await documents.UpdateDraftAsync(
            TradeCodes.ItemPriceUpdate,
            edited,
            TradePayloads.Payload(
                new { effective_date = today.AddDays(replacementOffset).ToString("yyyy-MM-dd") },
                TradePayloads.ItemPriceUpdateLines(new TradePayloads.ItemPriceUpdateLineRow(
                    Ordinal: 1, ItemId: key.ItemId, PriceTypeId: key.PriceTypeId, Currency: "USD", UnitPrice: 130m))),
            CancellationToken.None);
        await documents.PostAsync(TradeCodes.ItemPriceUpdate, edited, CancellationToken.None);

        var current = await ReadPricesAsync(services, key, today);
        current.ReportPrice.Should().Be(expectedCurrentPrice);
        current.LookupPrice.Should().Be(expectedCurrentPrice);
        var future = await services.GetRequiredService<ITradeCurrentItemPriceReader>().GetPageAsync(
            DateTime.UtcNow.AddDays(20), [key.ItemId], [key.PriceTypeId], 0, 20);
        var futureLookup = await services.GetRequiredService<ITradePricingLookupReader>()
            .GetLatestItemPricesAsync([key], today.AddDays(20));
        future.Rows.Single().UnitPrice.Should().Be(replacementOffset > -20 ? 130m : 100m);
        futureLookup[key].UnitPrice.Should().Be(future.Rows.Single().UnitPrice);
    }

    private static DimensionValue[] PriceDimensions(TradePriceLookupKey key) =>
    [
        new DimensionValue(DeterministicGuid.Create($"Dimension|{TradeCodes.Item}"), key.ItemId),
        new DimensionValue(DeterministicGuid.Create($"Dimension|{TradeCodes.PriceType}"), key.PriceTypeId)
    ];

    private async Task<(decimal? ReportPrice, decimal? LookupPrice)> ReadPricesAsync(
        IServiceProvider services,
        TradePriceLookupKey key,
        DateOnly asOfDate)
    {
        var report = await services.GetRequiredService<ITradeCurrentItemPriceReader>().GetPageAsync(
            DateTime.UtcNow.AddMinutes(1), [key.ItemId], [key.PriceTypeId], 0, 20);
        var prices = await services.GetRequiredService<ITradePricingLookupReader>()
            .GetLatestItemPricesAsync([key], asOfDate);
        var reportPrice = report.Rows.SingleOrDefault()?.UnitPrice;
        var lookupPrice = prices.GetValueOrDefault(key)?.UnitPrice;
        output.WriteLine($"Current Item Prices: {reportPrice?.ToString() ?? "missing"}; Pricing Lookup: {lookupPrice?.ToString() ?? "missing"}.");
        return (reportPrice, lookupPrice);
    }

    private static async Task<TradePriceLookupKey> CreateItemAsync(IServiceProvider services)
    {
        await services.GetRequiredService<ITradeSetupService>().EnsureDefaultsAsync(CancellationToken.None);
        var catalogs = services.GetRequiredService<ICatalogService>();
        var units = await catalogs.GetPageAsync(
            TradeCodes.UnitOfMeasure, new PageRequestDto(Offset: 0, Limit: 50, Search: null), CancellationToken.None);
        var priceTypes = await catalogs.GetPageAsync(
            TradeCodes.PriceType, new PageRequestDto(Offset: 0, Limit: 50, Search: null), CancellationToken.None);
        var unitId = units.Items.Single(item => item.Display == "Each").Id;
        var priceTypeId = priceTypes.Items.Single(item => item.Display == "Retail").Id;
        var item = await catalogs.CreateAsync(
            TradeCodes.Item,
            TradePayloads.Payload(new
            {
                display = "RR consistency item",
                name = "RR consistency item",
                sku = "RR-CONSISTENCY",
                unit_of_measure_id = unitId,
                default_sales_price_type_id = priceTypeId,
                is_inventory_item = true,
                is_active = true
            }),
            CancellationToken.None);

        return new TradePriceLookupKey(item.Id, priceTypeId);
    }

    private static async Task<Guid> PostPriceAsync(
        IServiceProvider services,
        TradePriceLookupKey key,
        DateOnly effectiveDate,
        decimal price)
    {
        var documents = services.GetRequiredService<IDocumentService>();
        var draft = await documents.CreateDraftAsync(
            TradeCodes.ItemPriceUpdate,
            TradePayloads.Payload(
                new { effective_date = effectiveDate.ToString("yyyy-MM-dd") },
                TradePayloads.ItemPriceUpdateLines(
                    new TradePayloads.ItemPriceUpdateLineRow(
                        Ordinal: 1,
                        ItemId: key.ItemId,
                        PriceTypeId: key.PriceTypeId,
                        Currency: "USD",
                        UnitPrice: price))),
            CancellationToken.None);
        var posted = await documents.PostAsync(TradeCodes.ItemPriceUpdate, draft.Id, CancellationToken.None);
        posted.Status.Should().Be(DocumentStatus.Posted);
        return posted.Id;
    }
}
