using FluentAssertions;
using Moq;
using NGB.Contracts.Common;
using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.Persistence.Locks;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Contracts;
using NGB.PropertyManagement.Contracts.Receivables;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Receivables;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.PropertyManagement.Runtime.Receivables;
using NGB.PropertyManagement.Runtime.WorkCenter;
using NGB.Runtime.Documents;
using Xunit;

namespace NGB.PropertyManagement.Runtime.Tests.Receivables;

public sealed class ReceivablesFifoApplyExecuteServiceFullCoverageTests
{
    [Fact]
    public async Task Request_rejects_empty_payment_and_non_positive_max_applications()
    {
        var fixture = new Fixture();
        await AssertInvalid(() => fixture.Sut.ExecuteAsync(new ReceivablesFifoApplyExecuteRequest(Guid.Empty, null)));
        await AssertInvalid(() => fixture.Sut.ExecuteAsync(new ReceivablesFifoApplyExecuteRequest(fixture.CreditId, 0)));
        await AssertInvalid(() => fixture.Sut.ExecuteAsync(new ReceivablesFifoApplyExecuteRequest(fixture.CreditId, -1)));
        await AssertInvalid(() => fixture.Sut.ExecuteAsync(new ReceivablesFifoApplyExecuteRequest(
            fixture.CreditId,
            FifoApplyLimits.MaxAtomicApplications + 1)));
    }

    [Fact]
    public async Task Empty_plan_returns_without_transaction_or_notification()
    {
        var fixture = new Fixture();
        fixture.SetPlan(available: 7m, []);

        var result = await fixture.ExecuteAsync();

        result.TotalApplied.Should().Be(0m);
        result.RemainingCredit.Should().Be(7m);
        result.ExecutedApplies.Should().BeEmpty();
        fixture.Uow.Verify(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
        fixture.WorkCenter.VerifyNoOtherCalls();
        fixture.Suggest.Verify(x => x.SuggestAsync(
            It.Is<ReceivablesFifoApplySuggestRequest>(request =>
                request.MaxApplications == FifoApplyLimits.DefaultMaxAtomicApplications),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(5, 3, 2)]
    [InlineData(2, 3, 0)]
    public async Task Positive_lines_are_created_posted_and_notified_while_remaining_credit_is_clamped(
        decimal available,
        decimal amount,
        decimal expectedRemaining)
    {
        var fixture = new Fixture();
        var charge = Guid.CreateVersion7();
        var apply = Guid.CreateVersion7();
        var user = Guid.CreateVersion7();
        fixture.SetPlan(available,
        [
            Suggested(Guid.CreateVersion7(), 0m),
            Suggested(charge, amount)
        ]);
        fixture.Drafts.Setup(x => x.CreateDraftAsync(
                PropertyManagementCodes.ReceivableApply, null, It.IsAny<DateTime>(), false, false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(apply);
        fixture.WorkCenter.Setup(x => x.CompleteIfExhaustedAsync(fixture.CreditId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([user]);

        var result = await fixture.ExecuteAsync();

        result.TotalApplied.Should().Be(amount);
        result.RemainingCredit.Should().Be(expectedRemaining);
        result.ExecutedApplies.Single().Should().Be(new ReceivablesExecutedApplyDto(apply, charge, amount));
        fixture.Posting.Verify(x => x.PostAsync(apply, false, It.IsAny<CancellationToken>()), Times.Once);
        fixture.WorkCenter.Verify(x => x.NotifyChangedAsync(
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { user })),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Non_empty_plan_with_only_non_positive_lines_commits_without_completion_or_notification()
    {
        var fixture = new Fixture();
        fixture.SetPlan(4m, [Suggested(Guid.CreateVersion7(), 0m)]);

        var result = await fixture.ExecuteAsync();

        result.ExecutedApplies.Should().BeEmpty();
        result.RemainingCredit.Should().Be(4m);
        fixture.WorkCenter.Verify(x => x.CompleteIfExhaustedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.WorkCenter.Verify(x => x.NotifyChangedAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.Uow.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private static ReceivablesSuggestedApplyDto Suggested(Guid chargeId, decimal amount)
        => new(chargeId, 10m, new DateOnly(2026, 1, 1), amount, new RecordPayload());

    private static async Task AssertInvalid(Func<Task> action)
        => await action.Should().ThrowAsync<ReceivablesRequestValidationException>();

    private sealed class Fixture
    {
        public Fixture()
        {
            Documents.Setup(x => x.GetAsync(CreditId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DocumentRecord
                {
                    Id = CreditId,
                    TypeCode = PropertyManagementCodes.ReceivablePayment,
                    DateUtc = DateTime.UnixEpoch,
                    Status = DocumentStatus.Posted
                });
            Readers.Setup(x => x.ReadReceivablePaymentHeadAsync(CreditId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PmReceivablePaymentHead(
                    CreditId, Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), null,
                    new DateOnly(2026, 1, 15), 10m, null));
            SetPlan(0m, []);
            Uow.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Uow.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Uow.Setup(x => x.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Relationships.Setup(x => x.CreateAsync(
                    It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), false, It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            Sut = new ReceivablesFifoApplyExecuteService(
                Suggest.Object, Drafts.Object, Posting.Object, Relationships.Object, Heads.Object,
                Readers.Object, Documents.Object, Locks.Object, Uow.Object, WorkCenter.Object);
        }

        public Guid CreditId { get; } = Guid.CreateVersion7();
        public Guid RegisterId { get; } = Guid.CreateVersion7();
        public Mock<IReceivablesFifoApplySuggestService> Suggest { get; } = new();
        public Mock<IDocumentDraftService> Drafts { get; } = new();
        public Mock<IDocumentPostingService> Posting { get; } = new();
        public Mock<IDocumentRelationshipService> Relationships { get; } = new();
        public Mock<IReceivableApplyHeadWriter> Heads { get; } = new();
        public Mock<IPropertyManagementDocumentReaders> Readers { get; } = new();
        public Mock<IDocumentRepository> Documents { get; } = new();
        public Mock<IAdvisoryLockManager> Locks { get; } = new();
        public Mock<IUnitOfWork> Uow { get; } = new();
        public Mock<IReceivablePaymentWorkCenterSynchronizer> WorkCenter { get; } = new();
        public ReceivablesFifoApplyExecuteService Sut { get; }

        public void SetPlan(decimal available, IReadOnlyList<ReceivablesSuggestedApplyDto> suggested)
            => Suggest.Setup(x => x.SuggestAsync(
                    It.IsAny<ReceivablesFifoApplySuggestRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReceivablesFifoApplySuggestResponse(
                    CreditId, RegisterId, available, 10m, suggested.Sum(x => x.Amount), available, suggested));

        public Task<ReceivablesFifoApplyExecuteResponse> ExecuteAsync()
            => Sut.ExecuteAsync(new ReceivablesFifoApplyExecuteRequest(CreditId, null));
    }
}
