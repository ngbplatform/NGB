using FluentAssertions;
using Moq;
using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Contracts;
using NGB.PropertyManagement.Contracts.Receivables;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Receivables;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.PropertyManagement.Runtime.Receivables;
using NGB.Runtime.Documents;
using Xunit;

namespace NGB.PropertyManagement.Runtime.Tests.Receivables;

public sealed class ReceivablesFifoApplySuggestServiceFullCoverageTests
{
    [Fact]
    public async Task Single_credit_request_validates_id_limit_and_available_credit()
    {
        var fixture = new Fixture();
        await AssertInvalid(() => fixture.Sut.SuggestAsync(new ReceivablesFifoApplySuggestRequest(Guid.Empty, null)));
        await AssertInvalid(() => fixture.Sut.SuggestAsync(new ReceivablesFifoApplySuggestRequest(fixture.CreditId, 0)));
        await AssertInvalid(() => fixture.Sut.SuggestAsync(new ReceivablesFifoApplySuggestRequest(fixture.CreditId, -1)));
        await AssertInvalid(() => fixture.Sut.SuggestAsync(new ReceivablesFifoApplySuggestRequest(
            fixture.CreditId, FifoApplyLimits.MaxApplications + 1)));

        fixture.SetDetails([], []);
        await AssertInvalid(() => fixture.Sut.SuggestAsync(new ReceivablesFifoApplySuggestRequest(fixture.CreditId, null)));
        fixture.SetDetails([], [fixture.Credit(fixture.CreditId, 0m)]);
        await AssertInvalid(() => fixture.Sut.SuggestAsync(new ReceivablesFifoApplySuggestRequest(fixture.CreditId, null)));
    }

    [Fact]
    public async Task Single_credit_plan_maps_fifo_lines_totals_and_payload_without_writes()
    {
        var fixture = new Fixture();
        var charge1 = Guid.CreateVersion7();
        var charge2 = Guid.CreateVersion7();
        fixture.SetDetails(
            [fixture.Charge(charge1, 10m, new DateOnly(2026, 1, 1)), fixture.Charge(charge2, 10m, new DateOnly(2026, 2, 1))],
            [fixture.Credit(fixture.CreditId, 15m)]);

        var result = await fixture.Sut.SuggestAsync(new ReceivablesFifoApplySuggestRequest(fixture.CreditId, 1));

        result.CreditDocumentId.Should().Be(fixture.CreditId);
        result.RegisterId.Should().Be(fixture.RegisterId);
        result.AvailableCredit.Should().Be(15m);
        result.TotalOutstanding.Should().Be(20m);
        result.TotalApplied.Should().Be(10m);
        result.RemainingCredit.Should().Be(5m);
        result.SuggestedApplies.Should().ContainSingle();
        result.SuggestedApplies[0].ChargeDocumentId.Should().Be(charge1);
        result.SuggestedApplies[0].ApplyPayload.Fields.Should().ContainKeys(
            "credit_document_id", "charge_document_id", "applied_on_utc", "amount");
        fixture.Uow.Verify(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        fixture.Drafts.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Lease_request_validates_month_range_limit_and_empty_lease()
    {
        var fixture = new Fixture();
        await fixture.Sut.SuggestLeaseAsync(new ReceivablesSuggestFifoApplyRequest(fixture.LeaseId));
        await AssertInvalid(() => fixture.LeaseQueryAsync(leaseId: Guid.Empty));
        await AssertInvalid(() => fixture.LeaseQueryAsync(asOf: new DateOnly(2026, 1, 2)));
        await AssertInvalid(() => fixture.LeaseQueryAsync(to: new DateOnly(2026, 1, 2)));
        await AssertInvalid(() => fixture.LeaseQueryAsync(asOf: new DateOnly(2026, 2, 1), to: new DateOnly(2026, 1, 1)));
        await AssertInvalid(() => fixture.LeaseQueryAsync(limit: 0));
        await AssertInvalid(() => fixture.LeaseQueryAsync(limit: -1));
        await AssertInvalid(() => fixture.LeaseQueryAsync(limit: FifoApplyLimits.MaxApplications + 1));
    }

    [Fact]
    public async Task Lease_plan_reports_empty_limit_and_remaining_warnings_and_preserves_credit_type()
    {
        var fixture = new Fixture();
        fixture.SetDetails(
            [fixture.Charge(Guid.CreateVersion7(), 0m)],
            [fixture.Credit(Guid.CreateVersion7(), 0m)]);
        var empty = await fixture.LeaseQueryAsync();
        empty.Warnings.Select(x => x.Code).Should().Equal("no_charges", "no_credits");
        empty.SuggestedApplies.Should().BeEmpty();

        var charge1 = Guid.CreateVersion7();
        var charge2 = Guid.CreateVersion7();
        var credit = Guid.CreateVersion7();
        fixture.SetDetails(
            [fixture.Charge(charge1, 10m), fixture.Charge(charge2, 10m)],
            [fixture.Credit(credit, 15m, PropertyManagementCodes.ReceivableCreditMemo)]);
        var limited = await fixture.LeaseQueryAsync(limit: 1);
        limited.TotalApplied.Should().Be(10m);
        limited.RemainingOutstanding.Should().Be(10m);
        limited.RemainingCredit.Should().Be(5m);
        limited.SuggestedApplies.Single().CreditDocumentType.Should().Be(PropertyManagementCodes.ReceivableCreditMemo);
        limited.Warnings.Select(x => x.Code).Should().Equal(
            "limit_reached", "outstanding_remaining", "credit_remaining");
    }

    [Fact]
    public async Task Lease_plan_optionally_materializes_each_draft_in_one_transaction()
    {
        var fixture = new Fixture();
        var credit = Guid.CreateVersion7();
        var apply1 = Guid.CreateVersion7();
        var apply2 = Guid.CreateVersion7();
        fixture.SetDetails(
            [fixture.Charge(Guid.CreateVersion7(), 4m), fixture.Charge(Guid.CreateVersion7(), 6m)],
            [fixture.Credit(credit, 10m)]);
        fixture.Drafts.SetupSequence(x => x.CreateDraftAsync(
                PropertyManagementCodes.ReceivableApply, null, It.IsAny<DateTime>(), false, false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(apply1)
            .ReturnsAsync(apply2);

        var result = await fixture.LeaseQueryAsync(createDrafts: true);

        result.SuggestedApplies.Select(x => x.ApplyId).Should().Equal(apply1, apply2);
        result.Warnings.Should().BeEmpty();
        fixture.Heads.Verify(x => x.UpsertAsync(
            It.IsAny<Guid>(), credit, It.IsAny<Guid>(), fixture.CreditDate, It.IsAny<decimal>(), null,
            It.IsAny<CancellationToken>()), Times.Exactly(2));
        fixture.Relationships.Verify(x => x.CreateAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), "based_on", false, It.IsAny<CancellationToken>()), Times.Exactly(4));
    }

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
                    CreditId, PartyId, PropertyId, LeaseId, null, CreditDate, 15m, "payment"));
            SetDetails([], []);
            Uow.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Uow.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Uow.Setup(x => x.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Relationships.Setup(x => x.CreateAsync(
                    It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), false, It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            Sut = new ReceivablesFifoApplySuggestService(
                Details.Object, Readers.Object, Documents.Object, Drafts.Object, Relationships.Object, Heads.Object, Uow.Object);
        }

        public Guid CreditId { get; } = Guid.CreateVersion7();
        public Guid PartyId { get; } = Guid.CreateVersion7();
        public Guid PropertyId { get; } = Guid.CreateVersion7();
        public Guid LeaseId { get; } = Guid.CreateVersion7();
        public Guid RegisterId { get; } = Guid.CreateVersion7();
        public DateOnly CreditDate { get; } = new(2026, 1, 15);
        public Mock<IReceivablesOpenItemsDetailsService> Details { get; } = new();
        public Mock<IPropertyManagementDocumentReaders> Readers { get; } = new();
        public Mock<IDocumentRepository> Documents { get; } = new();
        public Mock<IDocumentDraftService> Drafts { get; } = new();
        public Mock<IDocumentRelationshipService> Relationships { get; } = new();
        public Mock<IReceivableApplyHeadWriter> Heads { get; } = new();
        public Mock<IUnitOfWork> Uow { get; } = new();
        public ReceivablesFifoApplySuggestService Sut { get; }

        public void SetDetails(
            IReadOnlyList<ReceivablesOpenChargeItemDetailsDto> charges,
            IReadOnlyList<ReceivablesOpenCreditItemDetailsDto> credits)
            => Details.Setup(x => x.GetOpenItemsDetailsAsync(
                    It.IsAny<Guid>(), It.IsAny<Guid>(), LeaseId, It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReceivablesOpenItemsDetailsResponse(
                    RegisterId, PartyId, "Tenant", PropertyId, "Property", LeaseId, "Lease",
                    charges, credits, [], charges.Sum(x => x.OutstandingAmount), credits.Sum(x => x.AvailableCredit)));

        public ReceivablesOpenChargeItemDetailsDto Charge(Guid id, decimal outstanding, DateOnly? due = null)
            => new(id, PropertyManagementCodes.ReceivableCharge, null, $"Charge {id}",
                due ?? new DateOnly(2026, 1, 1), null, null, null, outstanding, outstanding);

        public ReceivablesOpenCreditItemDetailsDto Credit(
            Guid id,
            decimal available,
            string type = PropertyManagementCodes.ReceivablePayment)
            => new(id, type, null, $"Credit {id}", CreditDate, null, available, available);

        public Task<ReceivablesSuggestFifoApplyResponse> LeaseQueryAsync(
            Guid? leaseId = null,
            DateOnly? asOf = null,
            DateOnly? to = null,
            int? limit = null,
            bool createDrafts = false)
            => Sut.SuggestLeaseAsync(new ReceivablesSuggestFifoApplyRequest(
                leaseId ?? LeaseId,
                PartyId,
                PropertyId,
                asOf,
                to,
                limit,
                createDrafts));
    }
}
