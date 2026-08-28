using FluentAssertions;
using Moq;
using NGB.Persistence.Documents;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.PostgreSql.Documents;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Documents;

public sealed class PostingCachedPropertyManagementDocumentReadersTests
{
    [Fact]
    public async Task Single_reads_use_cache_and_query_or_bulk_reads_pass_through()
    {
        var inner = new Mock<IPropertyManagementDocumentReaders>();
        var cache = new RecordingCache();
        var sut = new PostingCachedPropertyManagementDocumentReaders(inner.Object, cache);
        var id = Guid.CreateVersion7();
        var ids = new[] { id };
        var day = new DateOnly(2026, 8, 27);

        await sut.ReadLeaseHeadAsync(id);
        await sut.ReadPropertyHeadAsync(id);
        await sut.FindFirstOverlappingPostedLeaseAsync(id, id, day, day);
        await sut.ReadMaintenanceRequestHeadAsync(id);
        await sut.ReadWorkOrderHeadAsync(id);
        await sut.ReadWorkOrderCompletionHeadAsync(id);
        await sut.ExistsOtherPostedWorkOrderCompletionAsync(id, id);
        await sut.ReadRentChargeHeadAsync(id);
        await sut.ReadReceivableChargeHeadAsync(id);
        await sut.ReadLateFeeChargeHeadAsync(id);
        await sut.ReadReceivablePaymentHeadAsync(id);
        await sut.ReadReceivableReturnedPaymentHeadAsync(id);
        await sut.ReadReceivableCreditMemoHeadAsync(id);
        await sut.ReadReceivableApplyHeadAsync(id);
        await sut.ReadPayableChargeHeadAsync(id);
        await sut.ReadPayablePaymentHeadAsync(id);
        await sut.ReadPayableCreditMemoHeadAsync(id);
        await sut.ReadPayableApplyHeadAsync(id);
        await sut.ReadReceivableChargeHeadsAsync(ids);
        await sut.ReadLateFeeChargeHeadsAsync(ids);
        await sut.ReadRentChargeHeadsAsync(ids);
        await sut.ReadReceivablePaymentHeadsAsync(ids);
        await sut.ReadReceivableCreditMemoHeadsAsync(ids);
        await sut.ReadPayableChargeHeadsAsync(ids);
        await sut.ReadPayablePaymentHeadsAsync(ids);
        await sut.ReadPayableCreditMemoHeadsAsync(ids);
        await sut.ReadActiveReceivableAllocationsAsync(id, id, id);
        await sut.ReadActivePayableAllocationsAsync(id, id, day, day);
        await sut.ReadFirstPayablesActivityMonthAsync(id, id);
        await sut.ReadChargeTypeHeadsAsync(ids);
        await sut.ReadChargeTypeHeadAsync(id);
        await sut.ReadPayableChargeTypeHeadsAsync(ids);
        await sut.ReadPayableChargeTypeHeadAsync(id);
        await sut.ReadDocumentInfosAsync(ids);

        cache.Keys.Should().HaveCount(18).And.OnlyHaveUniqueItems();
        cache.Keys.Should().OnlyContain(key => key.StartsWith("property-management:", StringComparison.Ordinal));
        inner.Verify(x => x.ReadDocumentInfosAsync(ids, It.IsAny<CancellationToken>()), Times.Once);
        inner.Verify(x => x.ReadActiveReceivableAllocationsAsync(id, id, id, It.IsAny<CancellationToken>()), Times.Once);
        inner.Verify(x => x.ReadActivePayableAllocationsAsync(id, id, day, day, It.IsAny<CancellationToken>()), Times.Once);
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
