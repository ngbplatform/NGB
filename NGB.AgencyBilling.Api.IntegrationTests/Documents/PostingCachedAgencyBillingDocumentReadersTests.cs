using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NGB.AgencyBilling.Documents;
using NGB.AgencyBilling.PostgreSql.DependencyInjection;
using NGB.AgencyBilling.PostgreSql.Documents;
using NGB.Persistence.Documents;
using NGB.Persistence.UnitOfWork;
using Xunit;

namespace NGB.AgencyBilling.Api.IntegrationTests.Documents;

public sealed class PostingCachedAgencyBillingDocumentReadersTests
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
        services.AddAgencyBillingPostgresModule();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var inner = scope.ServiceProvider.GetRequiredService<AgencyBillingDocumentReaders>();
        var resolved = scope.ServiceProvider.GetRequiredService<IAgencyBillingDocumentReaders>();

        if (withCache)
            resolved.Should().BeOfType<PostingCachedAgencyBillingDocumentReaders>().And.NotBeSameAs(inner);
        else
            resolved.Should().BeSameAs(inner);
    }

    [Fact]
    public async Task Single_reads_use_cache_and_bulk_reads_pass_through()
    {
        var inner = new Mock<IAgencyBillingDocumentReaders>();
        var cache = new RecordingCache();
        var sut = new PostingCachedAgencyBillingDocumentReaders(inner.Object, cache);
        var id = Guid.CreateVersion7();
        var ids = new[] { id };

        await sut.ReadClientContractHeadAsync(id);
        await sut.ReadClientContractLinesAsync(id);
        await sut.ReadTimesheetHeadAsync(id);
        await sut.ReadTimesheetHeadsAsync(ids);
        await sut.ReadTimesheetLinesAsync(id);
        await sut.ReadTimesheetLinesAsync(ids);
        await sut.ReadSalesInvoiceHeadAsync(id);
        await sut.ReadSalesInvoiceHeadsAsync(ids);
        await sut.ReadSalesInvoiceLinesAsync(id);
        await sut.ReadCustomerPaymentHeadAsync(id);
        await sut.ReadCustomerPaymentAppliesAsync(id);

        cache.Keys.Should().HaveCount(8).And.OnlyHaveUniqueItems();
        cache.Keys.Should().OnlyContain(key => key.StartsWith("agency-billing:", StringComparison.Ordinal));
        inner.Verify(x => x.ReadTimesheetHeadsAsync(ids, It.IsAny<CancellationToken>()), Times.Once);
        inner.Verify(x => x.ReadTimesheetLinesAsync(ids, It.IsAny<CancellationToken>()), Times.Once);
        inner.Verify(x => x.ReadSalesInvoiceHeadsAsync(ids, It.IsAny<CancellationToken>()), Times.Once);
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
