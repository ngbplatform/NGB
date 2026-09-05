using FluentAssertions;
using Moq;
using NGB.Core.Dimensions;
using NGB.Core.Documents;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.Documents;
using NGB.Persistence.Locks;
using NGB.Persistence.OperationalRegisters;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Posting;
using NGB.PropertyManagement.Runtime.Policy;
using NGB.Runtime.Dimensions;
using NGB.Runtime.Documents;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PropertyManagement.Runtime.Tests.Posting;

public sealed class PayableApplyOpenItemsPostingHandlerFullCoverageTests
{
    [Fact]
    public async Task Basic_apply_fields_are_validated_before_locks()
    {
        var zero = new Fixture();
        zero.SetApply(amount: 0m);
        await AssertFailureAsync(zero);
        zero.Locks.VerifyNoOtherCalls();

        var missingCredit = new Fixture();
        missingCredit.SetApply(creditId: Guid.Empty);
        await AssertFailureAsync(missingCredit);
        missingCredit.Locks.VerifyNoOtherCalls();

        var missingCharge = new Fixture();
        missingCharge.SetApply(chargeId: Guid.Empty);
        await AssertFailureAsync(missingCharge);
        missingCharge.Locks.VerifyNoOtherCalls();

        var same = new Fixture();
        same.SetApply(chargeId: same.CreditId);
        await AssertFailureAsync(same);
        same.Locks.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Draft_locks_in_first_order_creates_relationships_and_builds_two_movements()
    {
        var fixture = new Fixture(reverseIds: false)
        {
            ChargeNet = 10m,
            CreditNet = -10m
        };
        fixture.Sut.TypeCode.Should().Be(PropertyManagementCodes.PayableApply);

        await fixture.Sut.BuildMovementsAsync(
            fixture.Document(DocumentStatus.Draft), fixture.Builder.Object, default);

        fixture.Locks.Invocations.Select(x => (Guid)x.Arguments[0]).Should().Equal(fixture.CreditId, fixture.ChargeId);
        fixture.Relationships.Verify(x => x.CreateAsync(
            fixture.DocumentId, It.IsAny<Guid>(), "based_on", false, It.IsAny<CancellationToken>()), Times.Exactly(2));
        fixture.Movements.Should().HaveCount(2);
        fixture.Movements.Select(x => x.Resources["amount"]).Should().Equal(-5m, 5m);
        fixture.Bags.Should().HaveCount(2).And.OnlyContain(x => x.Count == 3);
    }

    [Fact]
    public async Task Repost_locks_in_reverse_order_skips_relationships_and_restores_pre_apply_balances()
    {
        var fixture = new Fixture(reverseIds: true)
        {
            ChargeNet = 0m,
            CreditNet = -5m
        };

        await fixture.Sut.BuildMovementsAsync(
            fixture.Document(DocumentStatus.Posted), fixture.Builder.Object, default);

        fixture.Locks.Invocations.Select(x => (Guid)x.Arguments[0]).Should().Equal(fixture.ChargeId, fixture.CreditId);
        fixture.Relationships.VerifyNoOtherCalls();
        fixture.Movements.Should().HaveCount(2);
    }

    [Fact]
    public async Task Credit_source_party_and_property_must_match_charge()
    {
        var party = new Fixture();
        party.SetPayment(partyId: Guid.CreateVersion7());
        await AssertFailureAsync(party);

        var property = new Fixture();
        property.SetPayment(propertyId: Guid.CreateVersion7());
        await AssertFailureAsync(property);
    }

    [Fact]
    public async Task Missing_operational_register_is_a_configuration_failure()
    {
        var fixture = new Fixture(hasRegister: false);

        var act = () => fixture.Sut.BuildMovementsAsync(
            fixture.Document(DocumentStatus.Draft), fixture.Builder.Object, default);

        await act.Should().ThrowAsync<NgbConfigurationViolationException>();
    }

    [Fact]
    public async Task Unexpected_dimension_set_count_is_rejected_before_balance_read()
    {
        var fixture = new Fixture();
        fixture.DimensionSets.Setup(x => x.GetOrCreateIdsAsync(
                It.IsAny<IReadOnlyList<DimensionBag>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([fixture.ChargeDimensionSetId]);

        var act = () => fixture.Sut.BuildMovementsAsync(
            fixture.Document(DocumentStatus.Draft), fixture.Builder.Object, default);

        await act.Should().ThrowAsync<NgbInvariantViolationException>()
            .WithMessage("*unexpected number of ids*");
        fixture.Net.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Charge_and_credit_boundaries_reject_over_application()
    {
        var charge = new Fixture { ChargeNet = 4m, CreditNet = -10m };
        await AssertFailureAsync(charge);

        var positiveCredit = new Fixture { ChargeNet = 10m, CreditNet = 1m };
        await AssertFailureAsync(positiveCredit);

        var insufficientCredit = new Fixture { ChargeNet = 10m, CreditNet = -4m };
        await AssertFailureAsync(insufficientCredit);

        var exact = new Fixture { ChargeNet = 5m, CreditNet = -5m };
        await exact.Sut.BuildMovementsAsync(exact.Document(DocumentStatus.Draft), exact.Builder.Object, default);
        exact.Movements.Should().HaveCount(2);
    }

    private static async Task AssertFailureAsync(Fixture fixture)
    {
        var act = () => fixture.Sut.BuildMovementsAsync(
            fixture.Document(DocumentStatus.Draft), fixture.Builder.Object, default);
        await act.Should().ThrowAsync<Exception>();
    }

    private sealed class Fixture
    {
        public Fixture(bool reverseIds = false, bool hasRegister = true)
        {
            CreditId = reverseIds
                ? Guid.Parse("00000000-0000-0000-0000-000000000020")
                : Guid.Parse("00000000-0000-0000-0000-000000000010");
            ChargeId = reverseIds
                ? Guid.Parse("00000000-0000-0000-0000-000000000010")
                : Guid.Parse("00000000-0000-0000-0000-000000000020");
            SetApply();
            SetPayment();
            SetCharge();
            Documents.Setup(x => x.GetAsync(CreditId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Document(CreditId, PropertyManagementCodes.PayablePayment, DocumentStatus.Posted));
            Policy.Setup(x => x.GetRequiredAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PropertyManagementAccountingPolicy(
                    Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
                    Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), RegisterId));
            Registers.Setup(x => x.GetByIdAsync(RegisterId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(hasRegister ? Register() : null);
            Locks.Setup(x => x.LockDocumentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            Relationships.Setup(x => x.CreateAsync(
                    It.IsAny<Guid>(), It.IsAny<Guid>(), "based_on", false, It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            DimensionSets.Setup(x => x.GetOrCreateIdsAsync(
                    It.IsAny<IReadOnlyList<DimensionBag>>(), It.IsAny<CancellationToken>()))
                .Callback<IReadOnlyList<DimensionBag>, CancellationToken>((bags, _) => Bags.AddRange(bags))
                .ReturnsAsync([ChargeDimensionSetId, CreditDimensionSetId]);
            Net.Setup(x => x.GetNetByDimensionSetsAsync(
                    RegisterId, It.IsAny<IReadOnlyCollection<Guid>>(), "amount", It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new Dictionary<Guid, decimal>
                {
                    [ChargeDimensionSetId] = ChargeNet,
                    [CreditDimensionSetId] = CreditNet
                });
            Builder.Setup(x => x.Add("pm.payables.open_items", It.IsAny<OperationalRegisterMovement>()))
                .Callback<string, OperationalRegisterMovement>((_, movement) => Movements.Add(movement));
            Sut = new PayableApplyOpenItemsOperationalRegisterPostingHandler(
                Readers.Object, Policy.Object, Registers.Object, Net.Object, DimensionSets.Object,
                Relationships.Object, Documents.Object, Locks.Object);
        }

        public Guid DocumentId { get; } = Guid.CreateVersion7();
        public Guid CreditId { get; }
        public Guid ChargeId { get; }
        public Guid PartyId { get; } = Guid.CreateVersion7();
        public Guid PropertyId { get; } = Guid.CreateVersion7();
        public Guid RegisterId { get; } = Guid.CreateVersion7();
        public Guid ChargeDimensionSetId { get; } = Guid.CreateVersion7();
        public Guid CreditDimensionSetId { get; } = Guid.CreateVersion7();
        public decimal ChargeNet { get; set; } = 10m;
        public decimal CreditNet { get; set; } = -10m;
        public List<DimensionBag> Bags { get; } = [];
        public List<OperationalRegisterMovement> Movements { get; } = [];
        public Mock<IPropertyManagementDocumentReaders> Readers { get; } = new(MockBehavior.Strict);
        public Mock<IPropertyManagementAccountingPolicyReader> Policy { get; } = new(MockBehavior.Strict);
        public Mock<IOperationalRegisterRepository> Registers { get; } = new(MockBehavior.Strict);
        public Mock<IOperationalRegisterResourceNetReader> Net { get; } = new(MockBehavior.Strict);
        public Mock<IDimensionSetService> DimensionSets { get; } = new(MockBehavior.Loose);
        public Mock<IDocumentRelationshipService> Relationships { get; } = new(MockBehavior.Strict);
        public Mock<IDocumentRepository> Documents { get; } = new(MockBehavior.Strict);
        public Mock<IAdvisoryLockManager> Locks { get; } = new(MockBehavior.Strict);
        public Mock<IOperationalRegisterMovementsBuilder> Builder { get; } = new(MockBehavior.Strict);
        public PayableApplyOpenItemsOperationalRegisterPostingHandler Sut { get; }

        public void SetApply(decimal amount = 5m, Guid? creditId = null, Guid? chargeId = null)
            => Readers.Setup(x => x.ReadPayableApplyHeadAsync(DocumentId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PmPayableApplyHead(
                    DocumentId, creditId ?? CreditId, chargeId ?? ChargeId, new DateOnly(2026, 8, 16), amount, null));

        public void SetPayment(Guid? partyId = null, Guid? propertyId = null)
            => Readers.Setup(x => x.ReadPayablePaymentHeadAsync(CreditId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PmPayablePaymentHead(
                    CreditId, partyId ?? PartyId, propertyId ?? PropertyId, null,
                    new DateOnly(2026, 8, 1), 10m, null));

        public void SetCharge()
            => Readers.Setup(x => x.ReadPayableChargeHeadAsync(ChargeId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PmPayableChargeHead(
                    ChargeId, PartyId, PropertyId, Guid.CreateVersion7(),
                    new DateOnly(2026, 8, 1), 10m, null, null));

        public DocumentRecord Document(DocumentStatus status)
            => Document(DocumentId, PropertyManagementCodes.PayableApply, status);

        private static DocumentRecord Document(Guid id, string type, DocumentStatus status)
            => new()
            {
                Id = id,
                TypeCode = type,
                DateUtc = DateTime.UnixEpoch,
                Status = status
            };

        private OperationalRegisterAdminItem Register()
            => new(RegisterId, "pm.payables.open_items", "pm.payables.open_items",
                "pm_payables_open_items", "Payables Open Items", false,
                DateTime.UnixEpoch, DateTime.UnixEpoch);
    }
}
