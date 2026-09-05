using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NGB.Persistence.UnitOfWork;
using NGB.Trade.Api.IntegrationTests.Infrastructure;
using NGB.Trade.PostgreSql.Pricing;
using NGB.Trade.PostgreSql.References;
using NGB.Trade.Pricing;
using Xunit;

namespace NGB.Trade.Api.IntegrationTests.Pricing;

[Collection(TradePostgresCollection.Name)]
public sealed class TradePricingLookupReaderFullCoverageTests(TradePostgresFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Empty_and_missing_price_paths_are_deterministic()
    {
        var dependencyFree = new TradePricingLookupReader(Mock.Of<IUnitOfWork>(MockBehavior.Strict));

        (await dependencyFree.GetItemSalesProfilesAsync([], CancellationToken.None)).Should().BeEmpty();
        (await dependencyFree.GetLatestItemPricesAsync([], DateOnly.MinValue, CancellationToken.None)).Should().BeEmpty();
        (await dependencyFree.GetLatestUnitCostsAsync([], DateOnly.MaxValue, CancellationToken.None)).Should().BeEmpty();

        var validationReader = new TradeCatalogValidationReader(Mock.Of<IUnitOfWork>(MockBehavior.Strict));
        (await validationReader.GetInventoryItemsAsync([Guid.Empty, Guid.Empty], CancellationToken.None))
            .Should().BeEmpty();

        using var host = TradeHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var reader = new TradePricingLookupReader(uow);
        var key = new TradePriceLookupKey(Guid.NewGuid(), Guid.NewGuid());

        (await reader.GetLatestItemPricesAsync([key], DateOnly.MaxValue, CancellationToken.None))
            .Should().BeEmpty();
    }

    [Fact]
    public void Price_row_mapping_covers_invalid_deleted_default_and_normalized_currency_boundaries()
    {
        var itemId = Guid.NewGuid();
        var priceTypeId = Guid.NewGuid();
        var sourceDocumentId = Guid.NewGuid();
        var effectiveDate = new DateOnly(2026, 8, 22);

        TradePricingLookupReader.MapItemPriceRow(
                itemId, priceTypeId, null, "usd", effectiveDate, sourceDocumentId, false)
            .Should().BeNull();
        TradePricingLookupReader.MapItemPriceRow(
                itemId, priceTypeId, 1m, "usd", null, sourceDocumentId, false)
            .Should().BeNull();
        TradePricingLookupReader.MapItemPriceRow(
                itemId, priceTypeId, 1m, "usd", effectiveDate, sourceDocumentId, true)
            .Should().BeNull();

        var defaultCurrency = TradePricingLookupReader.MapItemPriceRow(
            itemId, priceTypeId, 0m, " \t", effectiveDate, null, null);
        defaultCurrency.Should().NotBeNull();
        defaultCurrency!.Currency.Should().Be(TradeCodes.DefaultCurrency);
        defaultCurrency.UnitPrice.Should().Be(0m);
        defaultCurrency.SourceDocumentId.Should().BeNull();

        var normalized = TradePricingLookupReader.MapItemPriceRow(
            itemId, priceTypeId, decimal.MaxValue, " eur ", effectiveDate, sourceDocumentId, false);
        normalized.Should().Be(new TradeItemPriceSnapshot(
            itemId,
            priceTypeId,
            decimal.MaxValue,
            "EUR",
            effectiveDate,
            sourceDocumentId));
    }
}
