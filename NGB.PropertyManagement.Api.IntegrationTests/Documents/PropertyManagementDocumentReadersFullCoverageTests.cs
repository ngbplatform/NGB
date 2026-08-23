using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.PostgreSql.Documents;
using NGB.Runtime.UnitOfWork;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Documents;

[Collection(PmIntegrationCollection.Name)]
public sealed class PropertyManagementDocumentReadersFullCoverageTests(PmIntegrationFixture fixture)
    : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Empty_batch_and_required_filter_guards_return_without_querying_the_database()
    {
        var uow = new Mock<IUnitOfWork>(MockBehavior.Strict);
        uow.Setup(x => x.EnsureActiveTransaction());
        uow.Setup(x => x.EnsureConnectionOpenAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var sut = new PropertyManagementDocumentReaders(uow.Object);
        var id = Guid.NewGuid();

        (await sut.ReadPayableChargeHeadsAsync(null!, CancellationToken.None)).Should().BeEmpty();
        (await sut.ReadPayableChargeHeadsAsync([], CancellationToken.None)).Should().BeEmpty();
        (await sut.ReadPayableChargeTypeHeadsAsync(null!, CancellationToken.None)).Should().BeEmpty();
        (await sut.ReadPayableChargeTypeHeadsAsync([], CancellationToken.None)).Should().BeEmpty();
        (await sut.ReadDocumentInfosAsync(null!, CancellationToken.None)).Should().BeEmpty();
        (await sut.ReadDocumentInfosAsync([], CancellationToken.None)).Should().BeEmpty();

        (await sut.ReadActiveReceivableAllocationsAsync(Guid.Empty, id, id, CancellationToken.None)).Should().BeEmpty();
        (await sut.ReadActiveReceivableAllocationsAsync(id, Guid.Empty, id, CancellationToken.None)).Should().BeEmpty();
        (await sut.ReadActiveReceivableAllocationsAsync(id, id, Guid.Empty, CancellationToken.None)).Should().BeEmpty();
        (await sut.ReadActivePayableAllocationsAsync(Guid.Empty, id, ct: CancellationToken.None)).Should().BeEmpty();
        (await sut.ReadActivePayableAllocationsAsync(id, Guid.Empty, ct: CancellationToken.None)).Should().BeEmpty();
        (await sut.ReadFirstPayablesActivityMonthAsync(Guid.Empty, id, CancellationToken.None)).Should().BeNull();
        (await sut.ReadFirstPayablesActivityMonthAsync(id, Guid.Empty, CancellationToken.None)).Should().BeNull();

        uow.Verify(x => x.EnsureActiveTransaction(), Times.Exactly(13));
        uow.Verify(x => x.EnsureConnectionOpenAsync(It.IsAny<CancellationToken>()), Times.Exactly(13));
        uow.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Payable_allocations_accept_null_and_boundary_month_filters()
    {
        await using var factory = new PmApiFactory(fixture);
        await using var scope = factory.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var sut = scope.ServiceProvider.GetRequiredService<IPropertyManagementDocumentReaders>();

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            (await sut.ReadActivePayableAllocationsAsync(
                Guid.NewGuid(), Guid.NewGuid(), null, null, ct)).Should().BeEmpty();
            (await sut.ReadActivePayableAllocationsAsync(
                Guid.NewGuid(), Guid.NewGuid(), DateOnly.MinValue, DateOnly.MaxValue.AddMonths(-1), ct))
                .Should().BeEmpty();
        }, CancellationToken.None);
    }
}
