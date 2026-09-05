using FluentAssertions;
using Moq;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Contracts;
using NGB.PropertyManagement.Contracts.Payables;
using NGB.PropertyManagement.Payables;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.PropertyManagement.Runtime.Payables;
using NGB.Runtime.Documents;
using Xunit;

namespace NGB.PropertyManagement.Runtime.Tests.Payables;

public sealed class PayablesFifoApplySuggestServiceFullCoverageTests
{
    [Fact]
    public async Task Request_validation_rejects_missing_context_invalid_months_range_and_limit()
    {
        var fixture = new Fixture();
        await AssertInvalid(() => fixture.QueryAsync(partyId: Guid.Empty));
        await AssertInvalid(() => fixture.QueryAsync(propertyId: Guid.Empty));
        await AssertInvalid(() => fixture.QueryAsync(asOf: new DateOnly(2026, 1, 2)));
        await AssertInvalid(() => fixture.QueryAsync(to: new DateOnly(2026, 1, 2)));
        await AssertInvalid(() => fixture.QueryAsync(asOf: new DateOnly(2026, 2, 1), to: new DateOnly(2026, 1, 1)));
        await AssertInvalid(() => fixture.QueryAsync(limit: 0));
        await AssertInvalid(() => fixture.QueryAsync(limit: -1));
        await AssertInvalid(() => fixture.QueryAsync(limit: FifoApplyLimits.MaxApplications + 1));
    }

    [Fact]
    public async Task Empty_or_non_positive_open_items_return_both_source_warnings_without_drafts()
    {
        var fixture = new Fixture();
        fixture.SetOpenItems(
            [fixture.Charge(Guid.CreateVersion7(), 0m), fixture.Charge(Guid.CreateVersion7(), -1m)],
            [fixture.Credit(Guid.CreateVersion7(), 0m), fixture.Credit(Guid.CreateVersion7(), -1m)]);

        var result = await fixture.QueryAsync(createDrafts: true);

        result.SuggestedApplies.Should().BeEmpty();
        result.TotalApplied.Should().Be(0m);
        result.Warnings.Select(x => x.Code).Should().Equal("no_charges", "no_credits");
        fixture.Uow.Verify(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Limited_plan_maps_payload_and_reports_limit_outstanding_and_credit_remainders()
    {
        var fixture = new Fixture();
        var charge1 = Guid.CreateVersion7();
        var charge2 = Guid.CreateVersion7();
        var credit = Guid.CreateVersion7();
        fixture.SetOpenItems(
            [fixture.Charge(charge1, 10m, new DateOnly(2026, 1, 1)), fixture.Charge(charge2, 10m, new DateOnly(2026, 2, 1))],
            [fixture.Credit(credit, 15m, new DateOnly(2026, 1, 15))]);

        var result = await fixture.QueryAsync(
            asOf: new DateOnly(2026, 1, 1),
            to: new DateOnly(2026, 2, 1),
            limit: 1);

        result.RegisterId.Should().Be(fixture.RegisterId);
        result.VendorId.Should().Be(fixture.PartyId);
        result.PropertyId.Should().Be(fixture.PropertyId);
        result.TotalApplied.Should().Be(10m);
        result.RemainingOutstanding.Should().Be(10m);
        result.RemainingCredit.Should().Be(5m);
        result.SuggestedApplies.Should().ContainSingle();
        var suggestion = result.SuggestedApplies.Single();
        suggestion.ApplyId.Should().BeNull();
        suggestion.CreditDocumentId.Should().Be(credit);
        suggestion.ChargeDocumentId.Should().Be(charge1);
        suggestion.CreditAmountBefore.Should().Be(15m);
        suggestion.CreditAmountAfter.Should().Be(5m);
        suggestion.ChargeOutstandingBefore.Should().Be(10m);
        suggestion.ChargeOutstandingAfter.Should().Be(0m);
        suggestion.ApplyPayload.Fields.Should().ContainKeys(
            "credit_document_id", "charge_document_id", "applied_on_utc", "amount");
        result.Warnings.Select(x => x.Code).Should().Equal(
            "limit_reached", "outstanding_remaining", "credit_remaining");
    }

    [Fact]
    public async Task Create_drafts_persists_every_suggestion_in_one_transaction_and_returns_ids()
    {
        var fixture = new Fixture();
        var charge1 = Guid.CreateVersion7();
        var charge2 = Guid.CreateVersion7();
        var credit = Guid.CreateVersion7();
        var apply1 = Guid.CreateVersion7();
        var apply2 = Guid.CreateVersion7();
        fixture.SetOpenItems(
            [fixture.Charge(charge1, 4m), fixture.Charge(charge2, 6m)],
            [fixture.Credit(credit, 10m)]);
        fixture.Drafts.SetupSequence(x => x.CreateDraftAsync(
                PropertyManagementCodes.PayableApply,
                null,
                It.IsAny<DateTime>(),
                false,
                false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(apply1)
            .ReturnsAsync(apply2);

        var result = await fixture.QueryAsync(createDrafts: true);

        result.SuggestedApplies.Select(x => x.ApplyId).Should().Equal(apply1, apply2);
        result.TotalApplied.Should().Be(10m);
        result.RemainingOutstanding.Should().Be(0m);
        result.RemainingCredit.Should().Be(0m);
        result.Warnings.Should().BeEmpty();
        fixture.Heads.Verify(x => x.UpsertAsync(
            It.IsAny<Guid>(), credit, It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<decimal>(), null,
            It.IsAny<CancellationToken>()), Times.Exactly(2));
        fixture.Relationships.Verify(x => x.CreateAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), "based_on", false, It.IsAny<CancellationToken>()), Times.Exactly(4));
        fixture.Uow.Verify(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        fixture.Uow.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private static async Task AssertInvalid(Func<Task> action)
        => await action.Should().ThrowAsync<PayablesRequestValidationException>();

    private sealed class Fixture
    {
        public Fixture()
        {
            SetOpenItems([], []);
            Uow.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Uow.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Uow.Setup(x => x.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Relationships.Setup(x => x.CreateAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), false, It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            Sut = new PayablesFifoApplySuggestService(
                Details.Object, Drafts.Object, Relationships.Object, Heads.Object, Uow.Object);
        }

        public Guid PartyId { get; } = Guid.CreateVersion7();
        public Guid PropertyId { get; } = Guid.CreateVersion7();
        public Guid RegisterId { get; } = Guid.CreateVersion7();
        public Mock<IPayablesOpenItemsDetailsService> Details { get; } = new();
        public Mock<IDocumentDraftService> Drafts { get; } = new();
        public Mock<IDocumentRelationshipService> Relationships { get; } = new();
        public Mock<IPayableApplyHeadWriter> Heads { get; } = new();
        public Mock<IUnitOfWork> Uow { get; } = new();
        public PayablesFifoApplySuggestService Sut { get; }

        public void SetOpenItems(
            IReadOnlyList<PayablesOpenChargeItemDetailsDto> charges,
            IReadOnlyList<PayablesOpenCreditItemDetailsDto> credits)
            => Details.Setup(x => x.GetOpenItemsDetailsAsync(
                    PartyId, PropertyId, It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PayablesOpenItemsDetailsResponse(
                    RegisterId, PartyId, "Vendor", PropertyId, "Property", charges, credits, [],
                    charges.Sum(x => x.OutstandingAmount), credits.Sum(x => x.AvailableCredit)));

        public PayablesOpenChargeItemDetailsDto Charge(Guid id, decimal outstanding, DateOnly? due = null)
            => new(id, PropertyManagementCodes.PayableCharge, null, $"Charge {id}",
                due ?? new DateOnly(2026, 1, 1), null, null, null, null, outstanding, outstanding);

        public PayablesOpenCreditItemDetailsDto Credit(Guid id, decimal available, DateOnly? date = null)
            => new(id, PropertyManagementCodes.PayablePayment, null, $"Credit {id}",
                date ?? new DateOnly(2026, 1, 1), null, available, available);

        public Task<PayablesSuggestFifoApplyResponse> QueryAsync(
            Guid? partyId = null,
            Guid? propertyId = null,
            DateOnly? asOf = null,
            DateOnly? to = null,
            int? limit = null,
            bool createDrafts = false)
            => Sut.SuggestAsync(new PayablesSuggestFifoApplyRequest(
                partyId ?? PartyId,
                propertyId ?? PropertyId,
                asOf,
                to,
                limit,
                createDrafts));
    }
}
