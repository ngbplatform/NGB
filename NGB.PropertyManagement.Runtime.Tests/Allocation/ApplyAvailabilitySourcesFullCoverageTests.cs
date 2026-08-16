using FluentAssertions;
using Moq;
using NGB.Application.Abstractions.Services;
using NGB.Core.Dimensions;
using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.Persistence.OperationalRegisters;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.PropertyManagement.Runtime.Payables;
using NGB.PropertyManagement.Runtime.Policy;
using NGB.PropertyManagement.Runtime.Receivables;
using Xunit;
using ActionDocumentStatus = NGB.Contracts.Metadata.DocumentStatus;
using StoredDocumentStatus = NGB.Core.Documents.DocumentStatus;

namespace NGB.PropertyManagement.Runtime.Tests.Allocation;

public sealed class ApplyAvailabilitySourcesFullCoverageTests
{
    private static readonly DateOnly Day = new(2026, 8, 16);

    [Fact]
    public async Task Payables_source_rejects_unsupported_and_non_posted_documents_without_reads()
    {
        var fixture = new PayablesFixture();

        var unsupported = await fixture.Sut.EvaluateAsync("other", fixture.DocumentId, ActionDocumentStatus.Posted, default);
        var draft = await fixture.Sut.EvaluateAsync(
            PropertyManagementCodes.PayableCharge, fixture.DocumentId, ActionDocumentStatus.Draft, default);

        DisabledCode(unsupported).Should().Be("pm.payables.apply.unsupported_document_type");
        DisabledCode(draft).Should().Be("pm.payables.apply.requires_posted");
        fixture.Readers.VerifyNoOtherCalls();
        fixture.Net.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Payables_charge_is_available_only_for_positive_outstanding_net()
    {
        var fixture = new PayablesFixture();
        fixture.Readers.Setup(x => x.ReadPayableChargeHeadAsync(fixture.DocumentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PmPayableChargeHead(fixture.DocumentId, fixture.PartyId, fixture.PropertyId,
                Guid.CreateVersion7(), Day, 10m, null, null));

        fixture.NetValue = 5m;
        var positive = await fixture.Sut.EvaluateAsync(
            PropertyManagementCodes.PayableCharge.ToUpperInvariant(), fixture.DocumentId, ActionDocumentStatus.Posted, default);
        fixture.NetValue = 0m;
        var zero = await fixture.Sut.EvaluateAsync(
            PropertyManagementCodes.PayableCharge, fixture.DocumentId, ActionDocumentStatus.Posted, default);
        fixture.NetValue = -2m;
        var negative = await fixture.Sut.EvaluateAsync(
            PropertyManagementCodes.PayableCharge, fixture.DocumentId, ActionDocumentStatus.Posted, default);

        positive.Should().BeSameAs(DocumentActionAvailabilityResult.Allowed);
        DisabledCode(zero).Should().Be("pm.payables.apply.no_outstanding");
        DisabledCode(negative).Should().Be("pm.payables.apply.no_outstanding");
        fixture.VerifyDimensions(expectedCount: 3);
    }

    [Fact]
    public async Task Payables_payment_and_credit_memo_are_available_only_for_negative_credit_net()
    {
        var fixture = new PayablesFixture();
        fixture.Readers.Setup(x => x.ReadPayablePaymentHeadAsync(fixture.DocumentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PmPayablePaymentHead(fixture.DocumentId, fixture.PartyId, fixture.PropertyId,
                null, Day, 10m, null));
        fixture.Readers.Setup(x => x.ReadPayableCreditMemoHeadAsync(fixture.DocumentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PmPayableCreditMemoHead(fixture.DocumentId, fixture.PartyId, fixture.PropertyId,
                Guid.CreateVersion7(), Day, 10m, null));

        fixture.NetValue = -3m;
        var payment = await fixture.Sut.EvaluateAsync(
            PropertyManagementCodes.PayablePayment, fixture.DocumentId, ActionDocumentStatus.Posted, default);
        fixture.NetValue = 2m;
        var memo = await fixture.Sut.EvaluateAsync(
            PropertyManagementCodes.PayableCreditMemo, fixture.DocumentId, ActionDocumentStatus.Posted, default);

        payment.Should().BeSameAs(DocumentActionAvailabilityResult.Allowed);
        DisabledCode(memo).Should().Be("pm.payables.apply.no_credit");
        fixture.VerifyDimensions(expectedCount: 3);
    }

    [Fact]
    public async Task Receivables_source_rejects_unsupported_and_non_posted_documents_without_reads()
    {
        var fixture = new ReceivablesFixture();

        var unsupported = await fixture.Sut.EvaluateAsync("other", fixture.DocumentId, ActionDocumentStatus.Posted, default);
        var draft = await fixture.Sut.EvaluateAsync(
            PropertyManagementCodes.ReceivableCharge, fixture.DocumentId, ActionDocumentStatus.Draft, default);

        DisabledCode(unsupported).Should().Be("pm.apply.unsupported_document_type");
        DisabledCode(draft).Should().Be("pm.receivables.apply.requires_posted");
        fixture.Readers.VerifyNoOtherCalls();
        fixture.Documents.VerifyNoOtherCalls();
        fixture.Net.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Every_receivables_charge_kind_uses_positive_net_as_outstanding()
    {
        var fixture = new ReceivablesFixture();
        fixture.Readers.Setup(x => x.ReadReceivableChargeHeadAsync(fixture.DocumentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PmReceivableChargeHead(fixture.DocumentId, fixture.PartyId, fixture.PropertyId,
                fixture.LeaseId, Guid.CreateVersion7(), Day, 10m, null));
        fixture.Readers.Setup(x => x.ReadRentChargeHeadAsync(fixture.DocumentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PmRentChargeHead(fixture.DocumentId, fixture.LeaseId, fixture.PartyId,
                fixture.PropertyId, Day, Day, Day, 10m, null));
        fixture.Readers.Setup(x => x.ReadLateFeeChargeHeadAsync(fixture.DocumentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PmLateFeeChargeHead(fixture.DocumentId, fixture.PartyId, fixture.PropertyId,
                fixture.LeaseId, Day, 10m, null));

        fixture.NetValue = 5m;
        var charge = await fixture.Sut.EvaluateAsync(
            PropertyManagementCodes.ReceivableCharge.ToUpperInvariant(), fixture.DocumentId, ActionDocumentStatus.Posted, default);
        fixture.NetValue = 0m;
        var rent = await fixture.Sut.EvaluateAsync(
            PropertyManagementCodes.RentCharge, fixture.DocumentId, ActionDocumentStatus.Posted, default);
        fixture.NetValue = -1m;
        var lateFee = await fixture.Sut.EvaluateAsync(
            PropertyManagementCodes.LateFeeCharge, fixture.DocumentId, ActionDocumentStatus.Posted, default);

        charge.Should().BeSameAs(DocumentActionAvailabilityResult.Allowed);
        DisabledCode(rent).Should().Be("pm.receivables.apply.no_outstanding");
        DisabledCode(lateFee).Should().Be("pm.receivables.apply.no_outstanding");
        fixture.VerifyDimensions(expectedCount: 4);
    }

    [Fact]
    public async Task Receivables_payment_and_credit_memo_use_negative_net_as_available_credit()
    {
        var fixture = new ReceivablesFixture();
        var paymentId = fixture.DocumentId;
        var memoId = Guid.CreateVersion7();
        fixture.Documents.Setup(x => x.GetAsync(paymentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Document(paymentId, PropertyManagementCodes.ReceivablePayment));
        fixture.Documents.Setup(x => x.GetAsync(memoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Document(memoId, PropertyManagementCodes.ReceivableCreditMemo));
        fixture.Readers.Setup(x => x.ReadReceivablePaymentHeadAsync(paymentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PmReceivablePaymentHead(paymentId, fixture.PartyId, fixture.PropertyId,
                fixture.LeaseId, null, Day, 10m, null));
        fixture.Readers.Setup(x => x.ReadReceivableCreditMemoHeadAsync(memoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PmReceivableCreditMemoHead(memoId, fixture.PartyId, fixture.PropertyId,
                fixture.LeaseId, null, Day, 10m, null));

        fixture.NetValue = -4m;
        var payment = await fixture.Sut.EvaluateAsync(
            PropertyManagementCodes.ReceivablePayment, paymentId, ActionDocumentStatus.Posted, default);
        fixture.NetValue = 1m;
        var memo = await fixture.Sut.EvaluateAsync(
            PropertyManagementCodes.ReceivableCreditMemo, memoId, ActionDocumentStatus.Posted, default);

        payment.Should().BeSameAs(DocumentActionAvailabilityResult.Allowed);
        DisabledCode(memo).Should().Be("pm.receivables.apply.no_credit");
        fixture.VerifyDimensions(expectedCount: 4);
    }

    [Fact]
    public async Task Lease_consistency_guard_covers_missing_party_property_and_valid_lease()
    {
        var documentId = Guid.CreateVersion7();
        var missingLeaseId = Guid.CreateVersion7();
        var partyMismatchLeaseId = Guid.CreateVersion7();
        var propertyMismatchLeaseId = Guid.CreateVersion7();
        var validLeaseId = Guid.CreateVersion7();
        var partyId = Guid.CreateVersion7();
        var propertyId = Guid.CreateVersion7();
        var readers = new Mock<IPropertyManagementDocumentReaders>(MockBehavior.Strict);
        readers.Setup(x => x.ReadLeaseHeadAsync(missingLeaseId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("missing"));
        readers.Setup(x => x.ReadLeaseHeadAsync(partyMismatchLeaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PmLeaseHead(partyMismatchLeaseId, Guid.CreateVersion7(), propertyId, Day, null));
        readers.Setup(x => x.ReadLeaseHeadAsync(propertyMismatchLeaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PmLeaseHead(propertyMismatchLeaseId, partyId, Guid.CreateVersion7(), Day, null));
        readers.Setup(x => x.ReadLeaseHeadAsync(validLeaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PmLeaseHead(validLeaseId, partyId, propertyId, Day, null));

        await ((Func<Task>)(() => LeaseConsistencyGuard.EnsureAsync(
                documentId, missingLeaseId, partyId, propertyId, readers.Object, default)))
            .Should().ThrowAsync<ReceivableLeaseConsistencyValidationException>();
        await ((Func<Task>)(() => LeaseConsistencyGuard.EnsureAsync(
                documentId, partyMismatchLeaseId, partyId, propertyId, readers.Object, default)))
            .Should().ThrowAsync<ReceivableLeaseConsistencyValidationException>();
        await ((Func<Task>)(() => LeaseConsistencyGuard.EnsureAsync(
                documentId, propertyMismatchLeaseId, partyId, propertyId, readers.Object, default)))
            .Should().ThrowAsync<ReceivableLeaseConsistencyValidationException>();
        await LeaseConsistencyGuard.EnsureAsync(
            documentId, validLeaseId, partyId, propertyId, readers.Object, default);
    }

    private static string DisabledCode(DocumentActionAvailabilityResult result)
        => result.DisabledReasons.Should().ContainSingle().Which.Code;

    private static DocumentRecord Document(Guid id, string type)
        => new()
        {
            Id = id,
            TypeCode = type,
            DateUtc = DateTime.UnixEpoch,
            Status = StoredDocumentStatus.Posted
        };

    private abstract class FixtureBase
    {
        protected FixtureBase()
        {
            Policy.Setup(x => x.GetRequiredAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
                new PropertyManagementAccountingPolicy(
                    Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
                    Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), RegisterId, RegisterId));
            Net.Setup(x => x.GetNetByDimensionsAsync(
                    RegisterId, It.IsAny<IReadOnlyList<DimensionValue>>(), "amount", It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => NetValue);
        }

        public Guid DocumentId { get; } = Guid.CreateVersion7();
        public Guid PartyId { get; } = Guid.CreateVersion7();
        public Guid PropertyId { get; } = Guid.CreateVersion7();
        public Guid LeaseId { get; } = Guid.CreateVersion7();
        public Guid RegisterId { get; } = Guid.CreateVersion7();
        public decimal NetValue { get; set; }
        public Mock<IPropertyManagementDocumentReaders> Readers { get; } = new(MockBehavior.Strict);
        public Mock<IPropertyManagementAccountingPolicyReader> Policy { get; } = new(MockBehavior.Strict);
        public Mock<IOperationalRegisterResourceNetReader> Net { get; } = new(MockBehavior.Strict);

        public void VerifyDimensions(int expectedCount)
            => Net.Verify(x => x.GetNetByDimensionsAsync(
                RegisterId,
                It.Is<IReadOnlyList<DimensionValue>>(values => values.Count == expectedCount),
                "amount",
                It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    private sealed class PayablesFixture : FixtureBase
    {
        public PayablesApplyAvailabilitySource Sut => new(Readers.Object, Policy.Object, Net.Object);
    }

    private sealed class ReceivablesFixture : FixtureBase
    {
        public Mock<IDocumentRepository> Documents { get; } = new(MockBehavior.Strict);
        public ReceivablesApplyAvailabilitySource Sut => new(Readers.Object, Documents.Object, Policy.Object, Net.Object);
    }
}
