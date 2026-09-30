using FluentAssertions;
using Moq;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.Contracts.Services;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.PropertyManagement.Runtime.Payables;
using NGB.PropertyManagement.Runtime.Receivables;
using NGB.PropertyManagement.Runtime.WorkCenter;
using NGB.Runtime.Documents;
using NGB.Runtime.Documents.Workflow;
using Xunit;

namespace NGB.PropertyManagement.Runtime.Tests.Allocation;

public sealed class UnapplyServicesFullCoverageTests
{
    [Fact]
    public async Task Payables_unapply_validates_id_and_posted_state()
    {
        var fixture = new Fixture();
        var sut = fixture.Payables();
        await ((Func<Task>)(() => sut.ExecuteAsync(Guid.Empty)))
            .Should().ThrowAsync<PayablesRequestValidationException>();

        fixture.SetDocument(PropertyManagementCodes.PayableApply, DocumentStatus.Draft);
        await ((Func<Task>)(() => sut.ExecuteAsync(fixture.ApplyId)))
            .Should().ThrowAsync<DocumentWorkflowStateMismatchException>();
    }

    [Fact]
    public async Task Payables_unapply_reads_head_unposts_and_maps_response()
    {
        var fixture = new Fixture();
        fixture.SetDocument(PropertyManagementCodes.PayableApply, DocumentStatus.Posted);
        fixture.Readers.Setup(x => x.ReadPayableApplyHeadAsync(fixture.ApplyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PmPayableApplyHead(
                fixture.ApplyId, fixture.CreditId, fixture.ChargeId, fixture.Day, 12m, "memo"));

        var result = await fixture.Payables().ExecuteAsync(fixture.ApplyId);

        result.ApplyId.Should().Be(fixture.ApplyId);
        result.CreditDocumentId.Should().Be(fixture.CreditId);
        result.ChargeDocumentId.Should().Be(fixture.ChargeId);
        result.AppliedOnUtc.Should().Be(fixture.Day);
        result.UnappliedAmount.Should().Be(12m);
        fixture.Posting.Verify(x => x.UnpostAsync(fixture.ApplyId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Receivables_unapply_validates_id_and_posted_state()
    {
        var fixture = new Fixture();
        var sut = fixture.Receivables();
        await ((Func<Task>)(() => sut.ExecuteAsync(Guid.Empty)))
            .Should().ThrowAsync<ReceivablesRequestValidationException>();

        fixture.SetDocument(PropertyManagementCodes.ReceivableApply, DocumentStatus.MarkedForDeletion);
        await ((Func<Task>)(() => sut.ExecuteAsync(fixture.ApplyId)))
            .Should().ThrowAsync<DocumentWorkflowStateMismatchException>();
    }

    [Fact]
    public async Task Receivables_unapply_unposts_synchronizes_and_notifies_changed_users()
    {
        var fixture = new Fixture();
        var user = Guid.CreateVersion7();
        fixture.SetDocument(PropertyManagementCodes.ReceivableApply, DocumentStatus.Posted);
        fixture.Readers.Setup(x => x.ReadReceivableApplyHeadAsync(fixture.ApplyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PmReceivableApplyHead(
                fixture.ApplyId, fixture.CreditId, fixture.ChargeId, fixture.Day, 15m, "memo"));
        fixture.WorkCenter.Setup(x => x.SynchronizeAsync(
                fixture.CreditId, It.IsAny<Guid?>(), fixture.ApplyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([user]);

        var result = await fixture.Receivables().ExecuteAsync(fixture.ApplyId);

        result.ApplyId.Should().Be(fixture.ApplyId);
        result.CreditDocumentId.Should().Be(fixture.CreditId);
        result.ChargeDocumentId.Should().Be(fixture.ChargeId);
        result.AppliedOnUtc.Should().Be(fixture.Day);
        result.UnappliedAmount.Should().Be(15m);
        fixture.Posting.Verify(x => x.UnpostAsync(fixture.ApplyId, false, It.IsAny<CancellationToken>()), Times.Once);
        fixture.WorkCenter.Verify(x => x.NotifyChangedAsync(
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { user })),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            Uow.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Uow.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Uow.Setup(x => x.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        }

        public Guid ApplyId { get; } = Guid.CreateVersion7();
        public Guid CreditId { get; } = Guid.CreateVersion7();
        public Guid ChargeId { get; } = Guid.CreateVersion7();
        public DateOnly Day { get; } = new(2026, 1, 15);
        public Mock<IDocumentService> Documents { get; } = new();
        public Mock<IDocumentPostingService> Posting { get; } = new();
        public Mock<IPropertyManagementDocumentReaders> Readers { get; } = new();
        public Mock<IUnitOfWork> Uow { get; } = new();
        public Mock<IReceivablePaymentWorkCenterSynchronizer> WorkCenter { get; } = new();

        public void SetDocument(string type, DocumentStatus status)
            => Documents.Setup(x => x.GetByIdAsync(type, ApplyId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DocumentDto(ApplyId, "Apply", new RecordPayload(), status, false));

        public PayablesUnapplyService Payables() => new(Documents.Object, Posting.Object, Readers.Object, Uow.Object);

        public ReceivablesUnapplyService Receivables()
            => new(Documents.Object, Posting.Object, Readers.Object, Uow.Object, WorkCenter.Object);
    }
}
