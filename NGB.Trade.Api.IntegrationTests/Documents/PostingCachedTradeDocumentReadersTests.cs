using FluentAssertions;
using Moq;
using NGB.Persistence.Documents;
using NGB.Trade.Documents;
using NGB.Trade.PostgreSql.Documents;
using Xunit;

namespace NGB.Trade.Api.IntegrationTests.Documents;

public sealed class PostingCachedTradeDocumentReadersTests
{
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
