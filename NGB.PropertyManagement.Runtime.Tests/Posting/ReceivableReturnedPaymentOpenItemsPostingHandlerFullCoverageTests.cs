using FluentAssertions;
using Moq;
using NGB.Core.Dimensions;
using NGB.Core.Documents;
using NGB.OperationalRegisters.Contracts;
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

public sealed class ReceivableReturnedPaymentOpenItemsPostingHandlerFullCoverageTests
{
    private static readonly DateOnly Day = new(2026, 8, 16);

    [Fact]
    public async Task Amount_and_original_payment_are_required_before_locking()
    {
        var zero = new Fixture();
        zero.SetReturned(amount: 0m);
        await AssertFailureAsync(zero);
        zero.Locks.VerifyNoOtherCalls();

        var missingOriginal = new Fixture();
        missingOriginal.SetReturned(originalPaymentId: Guid.Empty);
        await AssertFailureAsync(missingOriginal);
        missingOriginal.Locks.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Draft_creates_based_on_relationship_while_repost_does_not()
    {
        var draft = new Fixture();
        draft.NetValue = -10m;
        draft.Sut.TypeCode.Should().Be(PropertyManagementCodes.ReceivableReturnedPayment);
        await draft.Sut.BuildMovementsAsync(draft.Document(DocumentStatus.Draft), draft.Builder.Object, default);
        draft.Relationships.Verify(x => x.CreateAsync(
            draft.DocumentId, draft.OriginalPaymentId, "based_on", false, It.IsAny<CancellationToken>()), Times.Once);

        var posted = new Fixture();
        posted.NetValue = -5m;
        await posted.Sut.BuildMovementsAsync(posted.Document(DocumentStatus.Posted), posted.Builder.Object, default);
        posted.Relationships.VerifyNoOtherCalls();
        posted.Movement.Should().NotBeNull();
        posted.Movement!.Resources["amount"].Should().Be(5m);
        posted.Movement.OccurredAtUtc.Should().Be(Day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Original_payment_party_property_and_lease_must_match()
    {
        var party = new Fixture();
        party.SetOriginal(partyId: Guid.CreateVersion7());
        await AssertFailureAsync(party);

        var property = new Fixture();
        property.SetOriginal(propertyId: Guid.CreateVersion7());
        await AssertFailureAsync(property);

        var lease = new Fixture();
        lease.SetOriginal(leaseId: Guid.CreateVersion7());
        await AssertFailureAsync(lease);
    }

    [Fact]
    public async Task Returned_date_cannot_precede_original_payment_date()
    {
        var fixture = new Fixture();
        fixture.SetOriginal(receivedOn: Day.AddDays(1));

        await AssertFailureAsync(fixture);
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
    public async Task Positive_or_insufficient_negative_net_rejects_return_but_exact_credit_boundary_succeeds()
    {
        var positive = new Fixture { NetValue = 1m };
        await AssertFailureAsync(positive);

        var insufficient = new Fixture { NetValue = -4m };
        await AssertFailureAsync(insufficient);

        var exact = new Fixture { NetValue = -5m };
        await exact.Sut.BuildMovementsAsync(exact.Document(DocumentStatus.Draft), exact.Builder.Object, default);

        exact.Movement.Should().NotBeNull();
        exact.CapturedBag!.Count.Should().Be(4);
        exact.RegisterCode.Should().Be("pm.receivables.open_items");
        exact.Locks.Verify(x => x.LockDocumentAsync(exact.OriginalPaymentId, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static async Task AssertFailureAsync(Fixture fixture)
    {
        var act = () => fixture.Sut.BuildMovementsAsync(fixture.Document(DocumentStatus.Draft), fixture.Builder.Object, default);
        await act.Should().ThrowAsync<Exception>();
    }

    private sealed class Fixture
    {
        public Fixture(bool hasRegister = true)
        {
            SetReturned();
            SetOriginal();
            Policy.Setup(x => x.GetRequiredAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PropertyManagementAccountingPolicy(
                    Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
                    Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), RegisterId, Guid.CreateVersion7()));
            Registers.Setup(x => x.GetByIdAsync(RegisterId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(hasRegister ? Register() : null);
            Locks.Setup(x => x.LockDocumentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            Relationships.Setup(x => x.CreateAsync(
                    It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), false, It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            DimensionSets.Setup(x => x.GetOrCreateIdAsync(It.IsAny<DimensionBag>(), It.IsAny<CancellationToken>()))
                .Callback<DimensionBag, CancellationToken>((bag, _) => CapturedBag = bag)
                .ReturnsAsync(DimensionSetId);
            Net.Setup(x => x.GetNetByDimensionSetAsync(
                    RegisterId, DimensionSetId, "amount", It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => NetValue);
            Builder.Setup(x => x.Add(It.IsAny<string>(), It.IsAny<OperationalRegisterMovement>()))
                .Callback<string, OperationalRegisterMovement>((code, movement) =>
                {
                    RegisterCode = code;
                    Movement = movement;
                });
            Sut = new ReceivableReturnedPaymentOpenItemsOperationalRegisterPostingHandler(
                Readers.Object, Policy.Object, Registers.Object, Net.Object, DimensionSets.Object,
                Relationships.Object, Locks.Object);
        }

        public Guid DocumentId { get; } = Guid.CreateVersion7();
        public Guid OriginalPaymentId { get; } = Guid.CreateVersion7();
        public Guid PartyId { get; } = Guid.CreateVersion7();
        public Guid PropertyId { get; } = Guid.CreateVersion7();
        public Guid LeaseId { get; } = Guid.CreateVersion7();
        public Guid RegisterId { get; } = Guid.CreateVersion7();
        public Guid DimensionSetId { get; } = Guid.CreateVersion7();
        public decimal NetValue { get; set; } = -10m;
        public DimensionBag? CapturedBag { get; private set; }
        public string? RegisterCode { get; private set; }
        public OperationalRegisterMovement? Movement { get; private set; }
        public Mock<IPropertyManagementDocumentReaders> Readers { get; } = new(MockBehavior.Strict);
        public Mock<IPropertyManagementAccountingPolicyReader> Policy { get; } = new(MockBehavior.Strict);
        public Mock<IOperationalRegisterRepository> Registers { get; } = new(MockBehavior.Strict);
        public Mock<IOperationalRegisterResourceNetReader> Net { get; } = new(MockBehavior.Strict);
        public Mock<IDimensionSetService> DimensionSets { get; } = new(MockBehavior.Strict);
        public Mock<IDocumentRelationshipService> Relationships { get; } = new(MockBehavior.Strict);
        public Mock<IAdvisoryLockManager> Locks { get; } = new(MockBehavior.Strict);
        public Mock<IOperationalRegisterMovementsBuilder> Builder { get; } = new(MockBehavior.Strict);
        public ReceivableReturnedPaymentOpenItemsOperationalRegisterPostingHandler Sut { get; }

        public void SetReturned(decimal amount = 5m, Guid? originalPaymentId = null)
            => Readers.Setup(x => x.ReadReceivableReturnedPaymentHeadAsync(
                    DocumentId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PmReceivableReturnedPaymentHead(
                    DocumentId, PartyId, PropertyId, LeaseId, originalPaymentId ?? OriginalPaymentId,
                    null, Day, amount, null));

        public void SetOriginal(
            Guid? partyId = null,
            Guid? propertyId = null,
            Guid? leaseId = null,
            DateOnly? receivedOn = null)
            => Readers.Setup(x => x.ReadReceivablePaymentHeadAsync(
                    OriginalPaymentId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PmReceivablePaymentHead(
                    OriginalPaymentId, partyId ?? PartyId, propertyId ?? PropertyId, leaseId ?? LeaseId,
                    null, receivedOn ?? Day, 10m, null));

        public DocumentRecord Document(DocumentStatus status)
            => new()
            {
                Id = DocumentId,
                TypeCode = PropertyManagementCodes.ReceivableReturnedPayment,
                DateUtc = Day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                Status = status
            };

        private OperationalRegisterAdminItem Register()
            => new(RegisterId, "pm.receivables.open_items", "pm.receivables.open_items",
                "pm_receivables_open_items", "Receivables Open Items", false,
                DateTime.UnixEpoch, DateTime.UnixEpoch);
    }
}
