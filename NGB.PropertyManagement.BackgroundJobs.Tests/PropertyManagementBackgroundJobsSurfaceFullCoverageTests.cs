using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NGB.Application.Abstractions.Services;
using NGB.BackgroundJobs.Catalog;
using NGB.BackgroundJobs.Contracts;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.BackgroundJobs.Catalog;
using NGB.PropertyManagement.BackgroundJobs.DependencyInjection;
using NGB.PropertyManagement.BackgroundJobs.Jobs;
using NGB.PropertyManagement.BackgroundJobs.Services;
using NGB.Runtime.Documents;
using Xunit;

namespace NGB.PropertyManagement.BackgroundJobs.Tests;

public sealed class PropertyManagementBackgroundJobsSurfaceFullCoverageTests
{
    [Fact]
    public void CatalogAndModuleRegistration_AreCompleteIdempotentAndChainable()
    {
        PropertyManagementBackgroundJobCatalog.GenerateMonthlyRentCharges
            .Should().Be("pm.rent_charge.generate_monthly");
        PropertyManagementBackgroundJobCatalog.All.Should()
            .Equal(PropertyManagementBackgroundJobCatalog.GenerateMonthlyRentCharges);

        var services = new ServiceCollection();

        services.AddPropertyManagementBackgroundJobsModule().Should().BeSameAs(services);
        services.AddPropertyManagementBackgroundJobsModule().Should().BeSameAs(services);

        services.Count(x => x.ServiceType == typeof(GenerateMonthlyRentChargesService)).Should().Be(1);
        services.Count(x => x.ServiceType == typeof(IBackgroundJobCatalogContributor)).Should().Be(1);
        services.Count(x => x.ServiceType == typeof(IPlatformBackgroundJob)).Should().Be(1);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IBackgroundJobCatalogContributor>().GetJobIds().Should()
            .Equal(PropertyManagementBackgroundJobCatalog.GenerateMonthlyRentCharges);
    }

    [Fact]
    public async Task Job_UsesInjectedUtcClockAndForwardsCancellationToken()
    {
        var expectedDate = new DateOnly(2026, 8, 21);
        using var cancellation = new CancellationTokenSource();
        var uow = new Mock<IUnitOfWork>();
        uow.SetupGet(x => x.HasActiveTransaction).Returns(false);
        uow.Setup(x => x.BeginTransactionAsync(cancellation.Token)).Returns(Task.CompletedTask);
        uow.Setup(x => x.CommitAsync(cancellation.Token)).Returns(Task.CompletedTask);
        var reader = new Mock<IPropertyManagementRentChargeGenerationReader>(MockBehavior.Strict);
        reader.Setup(x => x.ReadPostedLeasesForMonthlyRentChargeGenerationAsync(
                expectedDate, null, null, It.IsAny<int>(), cancellation.Token))
            .ReturnsAsync(Array.Empty<PmRentChargeGenerationLease>());
        var service = new GenerateMonthlyRentChargesService(
            uow.Object,
            reader.Object,
            Mock.Of<IDocumentService>(),
            Mock.Of<IDocumentSystemLifecycleService>(),
            Mock.Of<IDocumentDraftService>(),
            NullLogger<GenerateMonthlyRentChargesService>.Instance);
        var sut = new GenerateMonthlyRentChargesJob(
            service,
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 21, 23, 59, 59, TimeSpan.Zero)));

        sut.JobId.Should().Be(PropertyManagementBackgroundJobCatalog.GenerateMonthlyRentCharges);
        await sut.RunAsync(cancellation.Token);

        reader.VerifyAll();
        uow.Verify(x => x.BeginTransactionAsync(cancellation.Token), Times.Once);
        uow.Verify(x => x.CommitAsync(cancellation.Token), Times.Once);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
