using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NGB.Persistence.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.Trade.Documents;
using NGB.Trade.PostgreSql.DependencyInjection;
using NGB.Trade.PostgreSql.Documents;
using Xunit;

namespace NGB.Trade.Api.IntegrationTests.Documents;

public sealed class PostingCachedTradeDocumentReadersTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Module_applies_posting_cache_decorator_only_when_cache_is_registered(bool withCache)
    {
        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<IUnitOfWork>());
        if (withCache)
            services.AddSingleton<IDocumentPostingReadCache>(new RecordingCache());
        services.AddTradePostgresModule();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var inner = scope.ServiceProvider.GetRequiredService<TradeDocumentReaders>();
        var resolved = scope.ServiceProvider.GetRequiredService<ITradeDocumentReaders>();

        if (withCache)
            resolved.Should().BeOfType<PostingCachedTradeDocumentReaders>().And.NotBeSameAs(inner);
        else
            resolved.Should().BeSameAs(inner);
    }

    [Fact]
    public async Task Every_single_document_read_uses_an_operation_specific_cache_key()
    {
        var inner = new Mock<ITradeDocumentReaders>();
        var cache = new RecordingCache();
        var sut = new PostingCachedTradeDocumentReaders(inner.Object, cache);
        var id = Guid.CreateVersion7();

        await sut.ReadPurchaseReceiptHeadAsync(id);
        await sut.ReadPurchaseReceiptLinesAsync(id);
        await sut.ReadSalesInvoiceHeadAsync(id);
        await sut.ReadSalesInvoiceLinesAsync(id);
        await sut.ReadInventoryTransferHeadAsync(id);
        await sut.ReadInventoryTransferLinesAsync(id);
        await sut.ReadInventoryAdjustmentHeadAsync(id);
        await sut.ReadInventoryAdjustmentLinesAsync(id);
        await sut.ReadCustomerReturnHeadAsync(id);
        await sut.ReadCustomerReturnLinesAsync(id);
        await sut.ReadVendorReturnHeadAsync(id);
        await sut.ReadVendorReturnLinesAsync(id);
        await sut.ReadCustomerPaymentHeadAsync(id);
        await sut.ReadVendorPaymentHeadAsync(id);
        await sut.ReadItemPriceUpdateHeadAsync(id);
        await sut.ReadItemPriceUpdateLinesAsync(id);

        cache.Keys.Should().HaveCount(16).And.OnlyHaveUniqueItems();
        cache.Keys.Should().OnlyContain(key => key.StartsWith("trade:", StringComparison.Ordinal));
    }

    private sealed class RecordingCache : IDocumentPostingReadCache
    {
        public List<string> Keys { get; } = [];
        public IDisposable BeginScope() => Mock.Of<IDisposable>();

        public async Task<T> GetOrAddAsync<T>(
            string key,
            Func<CancellationToken, Task<T>> valueFactory,
            CancellationToken ct = default)
        {
            Keys.Add(key);
            return await valueFactory(ct);
        }
    }
}
