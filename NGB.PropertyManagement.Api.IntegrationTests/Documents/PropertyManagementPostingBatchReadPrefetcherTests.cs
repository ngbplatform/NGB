using FluentAssertions;
using Moq;
using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.PostgreSql.Documents;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Documents;

public sealed class PropertyManagementPostingBatchReadPrefetcherTests
{
    [Fact]
    public async Task Prefetches_each_apply_type_once_and_primes_individual_read_keys()
    {
        var receivableId = Guid.NewGuid();
        var payableId = Guid.NewGuid();
        var receivableHead = new PmReceivableApplyHead(
            receivableId, Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 8, 30), 10m, null);
        var payableHead = new PmPayableApplyHead(
            payableId, Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 8, 30), 20m, "memo");
        var reader = new Mock<IPropertyManagementPostingBatchHeadReader>(MockBehavior.Strict);
        var cache = new RecordingCache();

        reader.Setup(x => x.ReadReceivableApplyHeadsAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { receivableId })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([receivableHead]);
        reader.Setup(x => x.ReadPayableApplyHeadsAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { payableId })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([payableHead]);

        var sut = new PropertyManagementPostingBatchReadPrefetcher(reader.Object, cache);
        await sut.PrefetchAsync([
            Document(receivableId, PropertyManagementCodes.ReceivableApply),
            Document(receivableId, PropertyManagementCodes.ReceivableApply.ToUpperInvariant()),
            Document(Guid.NewGuid(), PropertyManagementCodes.RentCharge),
            Document(payableId, PropertyManagementCodes.PayableApply)
        ]);

        cache.Values.Should().Contain(
            $"property-management:{nameof(IPropertyManagementDocumentReaders.ReadReceivableApplyHeadAsync)}:{receivableId:D}",
            receivableHead);
        cache.Values.Should().Contain(
            $"property-management:{nameof(IPropertyManagementDocumentReaders.ReadPayableApplyHeadAsync)}:{payableId:D}",
            payableHead);
        cache.Values.Should().HaveCount(2);
        reader.VerifyAll();
    }

    [Fact]
    public async Task Empty_or_irrelevant_batches_do_not_read_and_null_is_rejected()
    {
        var reader = new Mock<IPropertyManagementPostingBatchHeadReader>(MockBehavior.Strict);
        var sut = new PropertyManagementPostingBatchReadPrefetcher(reader.Object, new RecordingCache());

        await sut.PrefetchAsync([]);
        await sut.PrefetchAsync([Document(Guid.NewGuid(), PropertyManagementCodes.Lease)]);
        Func<Task> nullBatch = () => sut.PrefetchAsync(null!);

        await nullBatch.Should().ThrowAsync<ArgumentNullException>();
        reader.VerifyNoOtherCalls();
    }

    private static DocumentRecord Document(Guid id, string typeCode)
        => new()
        {
            Id = id,
            TypeCode = typeCode,
            DateUtc = DateTime.UtcNow,
            Status = DocumentStatus.Draft
        };

    private sealed class RecordingCache : IDocumentPostingReadCache
    {
        public Dictionary<string, object?> Values { get; } = new(StringComparer.Ordinal);

        public IDisposable BeginScope() => Mock.Of<IDisposable>();

        public Task<T> GetOrAddAsync<T>(
            string key,
            Func<CancellationToken, Task<T>> valueFactory,
            CancellationToken ct = default)
            => valueFactory(ct);

        public void Prime<T>(string key, T value) => Values.Add(key, value);
    }
}
