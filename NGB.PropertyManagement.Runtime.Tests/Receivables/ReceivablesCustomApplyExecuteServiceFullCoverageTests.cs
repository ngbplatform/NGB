using FluentAssertions;
using Moq;
using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.Persistence.Locks;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Contracts.Receivables;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Receivables;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.PropertyManagement.Runtime.Receivables;
using NGB.PropertyManagement.Runtime.WorkCenter;
using NGB.Runtime.Documents;
using Xunit;

namespace NGB.PropertyManagement.Runtime.Tests.Receivables;

public sealed class ReceivablesCustomApplyExecuteServiceFullCoverageTests
{
    [Fact]
    public async Task Request_validation_covers_missing_oversized_null_and_invalid_lines()
    {
        var fixture = new Fixture();
        await AssertRequestInvalid(() => fixture.ExecuteAsync(Guid.Empty, [fixture.Line()]));
        await AssertRequestInvalid(() => fixture.ExecuteAsync(fixture.CreditId, null!));
        await AssertRequestInvalid(() => fixture.ExecuteAsync(fixture.CreditId, []));
        await AssertRequestInvalid(() => fixture.ExecuteAsync(
            fixture.CreditId, Enumerable.Repeat(fixture.Line(), 501).ToArray()));
        await AssertRequestInvalid(() => fixture.ExecuteAsync(fixture.CreditId, [null!]));
        await AssertRequestInvalid(() => fixture.ExecuteAsync(fixture.CreditId, [fixture.Line(Guid.Empty)]));

        await ((Func<Task>)(() => fixture.ExecuteAsync(fixture.CreditId, [fixture.Line(fixture.CreditId)])))
            .Should().ThrowAsync<ReceivableApplyValidationException>();
        await ((Func<Task>)(() => fixture.ExecuteAsync(fixture.CreditId, [fixture.Line(amount: 0m)])))
            .Should().ThrowAsync<ReceivableApplyValidationException>();
        await ((Func<Task>)(() => fixture.ExecuteAsync(fixture.CreditId, [fixture.Line(amount: -1m)])))
            .Should().ThrowAsync<ReceivableApplyValidationException>();
    }

    [Fact]
    public async Task Credit_must_be_available_and_cover_total_requested_amount()
    {
        var fixture = new Fixture();
        fixture.SetOpen(credits: [], charges: [fixture.OpenCharge(fixture.ChargeId, 10m)]);
        await AssertApplyInvalid(() => fixture.ExecuteAsync(fixture.CreditId, [fixture.Line(amount: 1m)]));

        fixture.SetOpen(
            credits: [fixture.OpenCredit(0m)],
            charges: [fixture.OpenCharge(fixture.ChargeId, 10m)]);
        await AssertApplyInvalid(() => fixture.ExecuteAsync(fixture.CreditId, [fixture.Line(amount: 1m)]));

        fixture.SetOpen(
            credits: [fixture.OpenCredit(2m)],
            charges: [fixture.OpenCharge(fixture.ChargeId, 10m)]);
        await AssertApplyInvalid(() => fixture.ExecuteAsync(fixture.CreditId, [fixture.Line(amount: 3m)]));
    }

    [Fact]
    public async Task Every_charge_must_exist_and_have_enough_outstanding_amount()
    {
        var fixture = new Fixture();
        fixture.SetOpen(credits: [fixture.OpenCredit(10m)], charges: []);
        await AssertApplyInvalid(() => fixture.ExecuteAsync(fixture.CreditId, [fixture.Line(amount: 1m)]));

        fixture.SetOpen(
            credits: [fixture.OpenCredit(10m)],
            charges: [fixture.OpenCharge(fixture.ChargeId, 1m)]);
        await AssertApplyInvalid(() => fixture.ExecuteAsync(fixture.CreditId, [fixture.Line(amount: 2m)]));
    }

    [Fact]
    public async Task Valid_duplicate_lines_are_grouped_created_posted_and_notified()
    {
        var fixture = new Fixture(withPostingReadCache: true);
        const decimal available = 10m;
        const decimal requested = 7m;
        const decimal expectedRemaining = 3m;
        var otherCharge = Guid.CreateVersion7();
        var apply1 = Guid.CreateVersion7();
        var apply2 = Guid.CreateVersion7();
        var user = Guid.CreateVersion7();
        fixture.SetOpen(
            credits: [fixture.OpenCredit(available)],
            charges: [fixture.OpenCharge(fixture.ChargeId, 10m), fixture.OpenCharge(otherCharge, 10m)]);
        fixture.Drafts.SetupSequence(x => x.CreateDraftAsync(
                PropertyManagementCodes.ReceivableApply, null, It.IsAny<DateTime>(), false, false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(apply1)
            .ReturnsAsync(apply2);
        fixture.WorkCenter.Setup(x => x.CompleteIfExhaustedAsync(fixture.CreditId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([user]);
        var firstAmount = requested - 2m;

        var result = await fixture.ExecuteAsync(fixture.CreditId,
        [
            fixture.Line(otherCharge, 1m),
            fixture.Line(fixture.ChargeId, firstAmount / 2m),
            null!,
            fixture.Line(fixture.ChargeId, firstAmount / 2m),
            fixture.Line(otherCharge, 1m)
        ]);

        result.RegisterId.Should().Be(fixture.RegisterId);
        result.TotalApplied.Should().Be(requested);
        result.RemainingCredit.Should().Be(expectedRemaining);
        result.ExecutedApplies.Should().HaveCount(2);
        result.ExecutedApplies.Sum(x => x.Amount).Should().Be(requested);
        fixture.Heads.Verify(x => x.UpsertAsync(
            It.IsAny<Guid>(), fixture.CreditId, It.IsAny<Guid>(), fixture.CreditDate,
            It.IsAny<decimal>(), null, It.IsAny<CancellationToken>()), Times.Exactly(2));
        fixture.Posting.Verify(x => x.PostAsync(It.IsAny<Guid>(), false, It.IsAny<CancellationToken>()), Times.Exactly(2));
        fixture.WorkCenter.Verify(x => x.NotifyChangedAsync(
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { user })),
            It.IsAny<CancellationToken>()), Times.Once);
        fixture.PostingReadCache!.BeginScopeCount.Should().Be(1);
    }

    [Fact]
    public async Task Valid_lines_use_batch_posting_when_the_capability_is_available()
    {
        var fixture = new Fixture(withBatchPosting: true);
        var apply = Guid.CreateVersion7();
        fixture.SetOpen(
            credits: [fixture.OpenCredit(10m)],
            charges: [fixture.OpenCharge(fixture.ChargeId, 10m)]);
        fixture.Drafts.Setup(x => x.CreateDraftAsync(
                PropertyManagementCodes.ReceivableApply, null, It.IsAny<DateTime>(), false, false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(apply);
        fixture.BatchPosting!.Setup(x => x.PostManyAsync(
                It.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { apply })),
                false,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        fixture.WorkCenter.Setup(x => x.CompleteIfExhaustedAsync(
                fixture.CreditId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await fixture.ExecuteAsync(fixture.CreditId, [fixture.Line(amount: 2m)]);

        result.ExecutedApplies.Should().ContainSingle().Which.ApplyId.Should().Be(apply);
        fixture.BatchPosting.VerifyAll();
        fixture.Posting.Verify(x => x.PostAsync(
            It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static async Task AssertRequestInvalid(Func<Task> action)
        => await action.Should().ThrowAsync<ReceivablesRequestValidationException>();

    private static async Task AssertApplyInvalid(Func<Task> action)
        => await action.Should().ThrowAsync<ReceivableApplyValidationException>();

    private sealed class Fixture
    {
        public Fixture(bool withPostingReadCache = false, bool withBatchPosting = false)
        {
            if (withBatchPosting)
                BatchPosting = Posting.As<IDocumentPostingBatchService>();

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
                    CreditId, PartyId, PropertyId, LeaseId, null, CreditDate, 10m, null));
            SetOpen([OpenCredit(10m)], [OpenCharge(ChargeId, 10m)]);
            Uow.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Uow.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Uow.Setup(x => x.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            PostingReadCache = withPostingReadCache ? new RecordingPostingReadCache() : null;
            Sut = new ReceivablesCustomApplyExecuteService(
                OpenItems.Object, Drafts.Object, Posting.Object, Heads.Object,
                Readers.Object, Documents.Object, Locks.Object, Uow.Object, WorkCenter.Object,
                PostingReadCache);
        }

        public Guid CreditId { get; } = Guid.CreateVersion7();
        public Guid ChargeId { get; } = Guid.CreateVersion7();
        public Guid PartyId { get; } = Guid.CreateVersion7();
        public Guid PropertyId { get; } = Guid.CreateVersion7();
        public Guid LeaseId { get; } = Guid.CreateVersion7();
        public Guid RegisterId { get; } = Guid.CreateVersion7();
        public DateOnly CreditDate { get; } = new(2026, 1, 15);
        public Mock<IReceivablesOpenItemsService> OpenItems { get; } = new();
        public Mock<IDocumentDraftService> Drafts { get; } = new();
        public Mock<IDocumentPostingService> Posting { get; } = new();
        public Mock<IDocumentPostingBatchService>? BatchPosting { get; }
        public Mock<IReceivableApplyHeadWriter> Heads { get; } = new();
        public Mock<IPropertyManagementDocumentReaders> Readers { get; } = new();
        public Mock<IDocumentRepository> Documents { get; } = new();
        public Mock<IAdvisoryLockManager> Locks { get; } = new();
        public Mock<IUnitOfWork> Uow { get; } = new();
        public Mock<IReceivablePaymentWorkCenterSynchronizer> WorkCenter { get; } = new();
        public RecordingPostingReadCache? PostingReadCache { get; }
        public ReceivablesCustomApplyExecuteService Sut { get; }

        public void SetOpen(
            IReadOnlyList<ReceivablesOpenItemDto> credits,
            IReadOnlyList<ReceivablesOpenItemDto> charges)
            => OpenItems.Setup(x => x.GetOpenItemsAsync(PartyId, PropertyId, LeaseId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReceivablesOpenItemsResponse(
                    RegisterId, charges, credits, charges.Sum(x => x.Amount), credits.Sum(x => x.Amount)));

        public ReceivablesOpenItemDto OpenCredit(decimal amount) => new(CreditId, "Credit", amount);
        public ReceivablesOpenItemDto OpenCharge(Guid id, decimal amount) => new(id, "Charge", amount);
        public ReceivablesCustomApplyLine Line(Guid? charge = null, decimal amount = 1m)
            => new(charge ?? ChargeId, amount);

        public Task<ReceivablesCustomApplyExecuteResponse> ExecuteAsync(
            Guid creditId,
            IReadOnlyList<ReceivablesCustomApplyLine> lines)
            => Sut.ExecuteAsync(new ReceivablesCustomApplyExecuteRequest(creditId, lines));
    }

    private sealed class RecordingPostingReadCache : IDocumentPostingReadCache
    {
        public int BeginScopeCount { get; private set; }

        public IDisposable BeginScope()
        {
            BeginScopeCount++;
            return Mock.Of<IDisposable>();
        }

        public Task<T> GetOrAddAsync<T>(
            string key,
            Func<CancellationToken, Task<T>> valueFactory,
            CancellationToken ct = default)
            => valueFactory(ct);
    }
}
