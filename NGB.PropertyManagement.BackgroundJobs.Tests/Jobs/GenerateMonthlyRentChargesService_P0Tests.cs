using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.Contracts.Services;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.BackgroundJobs.Services;
using NGB.Runtime.Documents;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PropertyManagement.BackgroundJobs.Tests.Jobs;

public sealed class GenerateMonthlyRentChargesService_P0Tests
{
    [Fact]
    public async Task ExecuteAsync_CreatesOnlyMissingPeriods()
    {
        var leaseId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var asOfUtc = new DateOnly(2026, 4, 10);

        var uow = new Mock<IUnitOfWork>();
        uow.SetupGet(x => x.HasActiveTransaction).Returns(false);
        uow.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        uow.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        uow.Setup(x => x.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var reader = new Mock<IPropertyManagementRentChargeGenerationReader>();
        reader.Setup(x => x.ReadPostedLeasesForMonthlyRentChargeGenerationAsync(
                asOfUtc, null, null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new PmRentChargeGenerationLease(
                    leaseId,
                    new DateOnly(2026, 2, 1),
                    null,
                    1500.00m,
                    5)
            ]);
        reader.Setup(x => x.ReadExistingRentChargePeriodsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new PmRentChargePeriodKey(leaseId, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31))
            ]);

        var documents = new Mock<IDocumentService>();
        var lifecycle = new Mock<IDocumentSystemLifecycleService>();
        var drafts = new Mock<IDocumentDraftService>();

        var createdIds = new Queue<Guid>(
        [
            Guid.Parse("00000000-0000-0000-0000-000000000101"),
            Guid.Parse("00000000-0000-0000-0000-000000000102")
        ]);

        documents.Setup(x => x.CreateDraftAsync(PropertyManagementCodes.RentCharge, It.IsAny<RecordPayload>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new DocumentDto(
                createdIds.Dequeue(),
                Display: null,
                Payload: new RecordPayload(),
                Status: DocumentStatus.Draft,
                IsMarkedForDeletion: false));

        drafts.Setup(x => x.UpdateDraftAsync(It.IsAny<Guid>(), null, It.IsAny<DateTime?>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        lifecycle.Setup(x => x.PostAsync(PropertyManagementCodes.RentCharge, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, Guid id, CancellationToken _) => new DocumentDto(
                id,
                Display: null,
                Payload: new RecordPayload(),
                Status: DocumentStatus.Posted,
                IsMarkedForDeletion: false));

        var service = new GenerateMonthlyRentChargesService(
            uow.Object,
            reader.Object,
            documents.Object,
            lifecycle.Object,
            drafts.Object,
            NullLogger<GenerateMonthlyRentChargesService>.Instance);

        var result = await service.ExecuteAsync(asOfUtc, CancellationToken.None);

        result.CreatedCount.Should().Be(2);
        result.SkippedExistingCount.Should().Be(1);
        result.FailedCount.Should().Be(0);

        documents.Verify(x => x.CreateDraftAsync(PropertyManagementCodes.RentCharge, It.IsAny<RecordPayload>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        lifecycle.Verify(x => x.PostAsync(PropertyManagementCodes.RentCharge, It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        drafts.Verify(x => x.DeleteDraftAsync(It.IsAny<Guid>(), true, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenPostingFails_DeletesDraftAndThrowsUnexpected()
    {
        var leaseId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var draftId = Guid.Parse("00000000-0000-0000-0000-000000000201");
        var asOfUtc = new DateOnly(2026, 4, 10);

        var uow = new Mock<IUnitOfWork>();
        uow.SetupGet(x => x.HasActiveTransaction).Returns(false);
        uow.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        uow.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        uow.Setup(x => x.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var reader = new Mock<IPropertyManagementRentChargeGenerationReader>();
        reader.Setup(x => x.ReadPostedLeasesForMonthlyRentChargeGenerationAsync(
                asOfUtc, null, null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new PmRentChargeGenerationLease(
                    leaseId,
                    new DateOnly(2026, 4, 1),
                    null,
                    1750.00m,
                    5)
            ]);
        reader.Setup(x => x.ReadExistingRentChargePeriodsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PmRentChargePeriodKey>());

        var documents = new Mock<IDocumentService>();
        var lifecycle = new Mock<IDocumentSystemLifecycleService>();
        documents.Setup(x => x.CreateDraftAsync(PropertyManagementCodes.RentCharge, It.IsAny<RecordPayload>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentDto(
                draftId,
                Display: null,
                Payload: new RecordPayload(),
                Status: DocumentStatus.Draft,
                IsMarkedForDeletion: false));
        lifecycle.Setup(x => x.PostAsync(PropertyManagementCodes.RentCharge, draftId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var drafts = new Mock<IDocumentDraftService>();
        drafts.Setup(x => x.UpdateDraftAsync(draftId, null, It.IsAny<DateTime?>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        drafts.Setup(x => x.DeleteDraftAsync(draftId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = new GenerateMonthlyRentChargesService(
            uow.Object,
            reader.Object,
            documents.Object,
            lifecycle.Object,
            drafts.Object,
            NullLogger<GenerateMonthlyRentChargesService>.Instance);

        var act = () => service.ExecuteAsync(asOfUtc, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbUnexpectedException>();
        ex.Which.ErrorCode.Should().Be(NgbUnexpectedException.Code);

        drafts.Verify(x => x.DeleteDraftAsync(draftId, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCancellationArrivesDuringDraftCreation_RethrowsCancellation()
    {
        var asOfUtc = new DateOnly(2026, 4, 10);
        using var cancellation = new CancellationTokenSource();
        var uow = TransactionalUnitOfWork();
        var reader = RentChargeReader(asOfUtc, Guid.NewGuid());
        var documents = new Mock<IDocumentService>();
        documents.Setup(x => x.CreateDraftAsync(
                PropertyManagementCodes.RentCharge,
                It.IsAny<RecordPayload>(),
                cancellation.Token))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromException<DocumentDto>(new OperationCanceledException(cancellation.Token));
            });
        var drafts = new Mock<IDocumentDraftService>();
        var service = new GenerateMonthlyRentChargesService(
            uow.Object,
            reader.Object,
            documents.Object,
            Mock.Of<IDocumentSystemLifecycleService>(),
            drafts.Object,
            NullLogger<GenerateMonthlyRentChargesService>.Instance);

        var act = () => service.ExecuteAsync(asOfUtc, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        drafts.Verify(x => x.DeleteDraftAsync(It.IsAny<Guid>(), true, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenPostingAndDraftCleanupFail_ReportsOriginalFailureWithoutMaskingIt()
    {
        var asOfUtc = new DateOnly(2026, 4, 10);
        var draftId = Guid.NewGuid();
        var uow = TransactionalUnitOfWork();
        var reader = RentChargeReader(asOfUtc, Guid.NewGuid());
        var documents = new Mock<IDocumentService>();
        documents.Setup(x => x.CreateDraftAsync(
                PropertyManagementCodes.RentCharge,
                It.IsAny<RecordPayload>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentDto(
                draftId,
                Display: null,
                Payload: new RecordPayload(),
                Status: DocumentStatus.Draft,
                IsMarkedForDeletion: false));
        var drafts = new Mock<IDocumentDraftService>();
        drafts.Setup(x => x.UpdateDraftAsync(draftId, null, It.IsAny<DateTime?>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        drafts.Setup(x => x.DeleteDraftAsync(draftId, true, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("cleanup failed"));
        var lifecycle = new Mock<IDocumentSystemLifecycleService>();
        lifecycle.Setup(x => x.PostAsync(PropertyManagementCodes.RentCharge, draftId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("posting failed"));
        var service = new GenerateMonthlyRentChargesService(
            uow.Object,
            reader.Object,
            documents.Object,
            lifecycle.Object,
            drafts.Object,
            NullLogger<GenerateMonthlyRentChargesService>.Instance);

        var act = () => service.ExecuteAsync(asOfUtc, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<NgbUnexpectedException>();
        exception.Which.InnerException.Should().BeOfType<AggregateException>()
            .Which.InnerExceptions.Should().ContainSingle()
            .Which.Message.Should().Be("posting failed");
        drafts.Verify(x => x.DeleteDraftAsync(draftId, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static Mock<IUnitOfWork> TransactionalUnitOfWork()
    {
        var uow = new Mock<IUnitOfWork>();
        uow.SetupGet(x => x.HasActiveTransaction).Returns(false);
        uow.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        uow.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        uow.Setup(x => x.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return uow;
    }

    private static Mock<IPropertyManagementRentChargeGenerationReader> RentChargeReader(
        DateOnly asOfUtc,
        Guid leaseId)
    {
        var reader = new Mock<IPropertyManagementRentChargeGenerationReader>();
        reader.Setup(x => x.ReadPostedLeasesForMonthlyRentChargeGenerationAsync(
                asOfUtc, null, null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new PmRentChargeGenerationLease(
                    leaseId,
                    asOfUtc,
                    EndOnUtc: null,
                    RentAmount: 1000m,
                    DueDay: asOfUtc.Day)
            ]);
        reader.Setup(x => x.ReadExistingRentChargePeriodsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PmRentChargePeriodKey>());
        return reader;
    }
}
