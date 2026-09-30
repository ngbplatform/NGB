using System.Text.Json;
using FluentAssertions;
using Moq;
using NGB.Contracts.Common;
using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Documents.Validation;
using NGB.PropertyManagement.Runtime.Policy;
using NGB.Runtime.Documents.Validation;
using Xunit;

namespace NGB.PropertyManagement.Runtime.Tests.Documents.Validation;

public sealed class ReceivablePayloadValidatorsFullCoverageTests
{
    [Fact]
    public async Task Credit_memo_payload_covers_lease_classification_amount_property_and_updates()
    {
        var documentId = Guid.CreateVersion7();
        var leaseId = Guid.CreateVersion7();
        var chargeTypeId = Guid.CreateVersion7();
        var propertyId = Guid.CreateVersion7();
        var readers = new Mock<IPropertyManagementDocumentReaders>(MockBehavior.Strict);
        readers.Setup(x => x.ReadReceivableCreditMemoHeadAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PmReceivableCreditMemoHead(
                documentId, Guid.CreateVersion7(), propertyId, leaseId, chargeTypeId, DateOnly.MinValue, 10m, null));
        var documents = new Mock<IDocumentRepository>(MockBehavior.Strict);
        var leases = documents.SetupSequence(x => x.GetAsync(leaseId, It.IsAny<CancellationToken>()));
        leases.ReturnsAsync((DocumentRecord?)null)
            .ReturnsAsync(Document(leaseId, "wrong"))
            .ReturnsAsync(Document(leaseId, PropertyManagementCodes.Lease, DocumentStatus.MarkedForDeletion));
        for (var i = 0; i < 8; i++)
            leases.ReturnsAsync(Document(leaseId, PropertyManagementCodes.Lease));
        readers.SetupSequence(x => x.ReadChargeTypeHeadAsync(chargeTypeId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException())
            .ReturnsAsync(ChargeType())
            .ReturnsAsync(ChargeType())
            .ReturnsAsync(ChargeType())
            .ReturnsAsync(ChargeType())
            .ReturnsAsync(ChargeType())
            .ReturnsAsync(ChargeType())
            .ReturnsAsync(ChargeType());
        readers.Setup(x => x.ReadLeaseHeadAsync(leaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PmLeaseHead(leaseId, Guid.CreateVersion7(), propertyId, DateOnly.MinValue, null));
        readers.SetupSequence(x => x.ReadPropertyHeadAsync(propertyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PmPropertyHead?)null)
            .ReturnsAsync(Property(propertyId, deleted: true))
            .ReturnsAsync(Property(propertyId, kind: "Building"))
            .ReturnsAsync(Property(propertyId))
            .ReturnsAsync(Property(propertyId))
            .ReturnsAsync(Property(propertyId));
        var validator = new ReceivableCreditMemoPayloadValidator(readers.Object, documents.Object);

        validator.TypeCode.Should().Be(PropertyManagementCodes.ReceivableCreditMemo);
        for (var i = 0; i < 4; i++)
            await AssertInvalid(() => Create(validator, Full(10m)));
        await AssertInvalid(() => Create(validator, Full(0m)));
        for (var i = 0; i < 3; i++)
            await AssertInvalid(() => Create(validator, Full(10m)));
        await Create(validator, Full(10m));
        await Update(validator, documentId, Payload(("amount", 20m)));
        await Update(validator, documentId, Payload(("lease_id", leaseId)));

        var incompleteReaders = new Mock<IPropertyManagementDocumentReaders>(MockBehavior.Strict);
        incompleteReaders.Setup(x => x.ReadReceivableCreditMemoHeadAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PmReceivableCreditMemoHead(
                documentId, Guid.CreateVersion7(), propertyId, leaseId, null, DateOnly.MinValue, 10m, null));
        var incompleteValidator = new ReceivableCreditMemoPayloadValidator(
            incompleteReaders.Object,
            new Mock<IDocumentRepository>(MockBehavior.Strict).Object);
        await Update(incompleteValidator, documentId, Payload(("amount", 20m)));
        return;

        PmChargeTypeHead ChargeType() => new(chargeTypeId, "Rent", Guid.CreateVersion7());
        RecordPayload Full(decimal amount) => Payload(
            ("lease_id", leaseId), ("charge_type_id", chargeTypeId), ("amount", amount));
    }

    [Fact]
    public async Task Payment_payload_covers_lease_amount_property_optional_bank_and_updates()
    {
        var documentId = Guid.CreateVersion7();
        var leaseId = Guid.CreateVersion7();
        var propertyId = Guid.CreateVersion7();
        var bankId = Guid.CreateVersion7();
        var readers = new Mock<IPropertyManagementDocumentReaders>(MockBehavior.Strict);
        readers.Setup(x => x.ReadReceivablePaymentHeadAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PmReceivablePaymentHead(
                documentId, Guid.CreateVersion7(), propertyId, leaseId, bankId, DateOnly.MinValue, 10m, null));
        var documents = new Mock<IDocumentRepository>(MockBehavior.Strict);
        var leases = documents.SetupSequence(x => x.GetAsync(leaseId, It.IsAny<CancellationToken>()));
        leases.ReturnsAsync((DocumentRecord?)null)
            .ReturnsAsync(Document(leaseId, "wrong"))
            .ReturnsAsync(Document(leaseId, PropertyManagementCodes.Lease, DocumentStatus.MarkedForDeletion));
        for (var i = 0; i < 10; i++)
            leases.ReturnsAsync(Document(leaseId, PropertyManagementCodes.Lease));
        readers.Setup(x => x.ReadLeaseHeadAsync(leaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PmLeaseHead(leaseId, Guid.CreateVersion7(), propertyId, DateOnly.MinValue, null));
        var properties = readers.SetupSequence(x => x.ReadPropertyHeadAsync(propertyId, It.IsAny<CancellationToken>()));
        properties.ReturnsAsync((PmPropertyHead?)null)
            .ReturnsAsync(Property(propertyId, deleted: true))
            .ReturnsAsync(Property(propertyId, kind: "Building"));
        for (var i = 0; i < 6; i++)
            properties.ReturnsAsync(Property(propertyId));
        var banks = new Mock<IPropertyManagementBankAccountReader>(MockBehavior.Strict);
        banks.SetupSequence(x => x.TryGetAsync(bankId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PropertyManagementBankAccount?)null)
            .ReturnsAsync(Bank(bankId, deleted: true))
            .ReturnsAsync(Bank(bankId))
            .ReturnsAsync(Bank(bankId))
            .ReturnsAsync(Bank(bankId));
        var validator = new ReceivablePaymentPayloadValidator(readers.Object, documents.Object, banks.Object);

        validator.TypeCode.Should().Be(PropertyManagementCodes.ReceivablePayment);
        for (var i = 0; i < 3; i++)
            await AssertInvalid(() => Create(validator, Full(10m, bankId)));
        await AssertInvalid(() => Create(validator, Full(0m, bankId)));
        for (var i = 0; i < 5; i++)
            await AssertInvalid(() => Create(validator, Full(10m, bankId)));
        await Create(validator, Full(10m, bankId: null));
        await Create(validator, Full(10m, bankId));
        await Update(validator, documentId, Payload(("amount", 20m)));
        await Update(validator, documentId, Payload(("bank_account_id", null), ("lease_id", leaseId)));
        return;

        RecordPayload Full(decimal amount, Guid? bankId) => Payload(
            ("lease_id", leaseId), ("bank_account_id", bankId), ("amount", amount));
    }

    [Fact]
    public async Task Returned_payment_payload_covers_original_payment_lease_amount_date_and_updates()
    {
        var documentId = Guid.CreateVersion7();
        var originalPaymentId = Guid.CreateVersion7();
        var leaseId = Guid.CreateVersion7();
        var receivedOn = new DateOnly(2026, 8, 15);
        var returnedOn = receivedOn.AddDays(1);
        var readers = new Mock<IPropertyManagementDocumentReaders>(MockBehavior.Strict);
        readers.Setup(x => x.ReadReceivableReturnedPaymentHeadAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PmReceivableReturnedPaymentHead(
                documentId, Guid.CreateVersion7(), Guid.CreateVersion7(), leaseId, originalPaymentId,
                null, returnedOn, 10m, null));
        var documents = new Mock<IDocumentRepository>(MockBehavior.Strict);
        var originals = documents.SetupSequence(x => x.GetAsync(originalPaymentId, It.IsAny<CancellationToken>()));
        originals.ReturnsAsync((DocumentRecord?)null)
            .ReturnsAsync(Document(originalPaymentId, "wrong"));
        for (var i = 0; i < 8; i++)
            originals.ReturnsAsync(Document(originalPaymentId, PropertyManagementCodes.ReceivablePayment));
        readers.Setup(x => x.ReadReceivablePaymentHeadAsync(originalPaymentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PmReceivablePaymentHead(
                originalPaymentId, Guid.CreateVersion7(), Guid.CreateVersion7(), leaseId, null, receivedOn, 10m, null));
        documents.SetupSequence(x => x.GetAsync(leaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentRecord?)null)
            .ReturnsAsync(Document(leaseId, "wrong"))
            .ReturnsAsync(Document(leaseId, PropertyManagementCodes.Lease, DocumentStatus.MarkedForDeletion))
            .ReturnsAsync(Document(leaseId, PropertyManagementCodes.Lease))
            .ReturnsAsync(Document(leaseId, PropertyManagementCodes.Lease))
            .ReturnsAsync(Document(leaseId, PropertyManagementCodes.Lease))
            .ReturnsAsync(Document(leaseId, PropertyManagementCodes.Lease));
        var validator = new ReceivableReturnedPaymentPayloadValidator(readers.Object, documents.Object);

        validator.TypeCode.Should().Be(PropertyManagementCodes.ReceivableReturnedPayment);
        for (var i = 0; i < 2; i++)
            await AssertInvalid(() => Create(validator, Full(10m, returnedOn)));
        await AssertInvalid(() => Create(validator, Full(0m, returnedOn)));
        for (var i = 0; i < 3; i++)
            await AssertInvalid(() => Create(validator, Full(10m, returnedOn)));
        await AssertInvalid(() => Create(validator, Full(10m, receivedOn.AddDays(-1))));
        await Create(validator, Full(10m, returnedOn));
        await Update(validator, documentId, Payload(("amount", 20m)));
        await Update(validator, documentId, Payload(("returned_on_utc", returnedOn)));
        return;

        RecordPayload Full(decimal amount, DateOnly date) => Payload(
            ("original_payment_id", originalPaymentId), ("returned_on_utc", date), ("amount", amount));
    }

    private static PmPropertyHead Property(Guid id, string kind = "Unit", bool deleted = false) => new(id, kind, null, deleted);

    private static PropertyManagementBankAccount Bank(Guid id, bool deleted = false)
        => new(id, "Operating", Guid.CreateVersion7(), false, deleted);

    private static RecordPayload Payload(params (string Key, object? Value)[] values)
        => new(values.ToDictionary(
            pair => pair.Key,
            pair => JsonSerializer.SerializeToElement(pair.Value),
            StringComparer.Ordinal));

    private static Task Create(IDocumentDraftPayloadValidator validator, RecordPayload payload)
        => validator.ValidateCreateDraftPayloadAsync(payload, EmptyParts, default);

    private static Task Update(IDocumentDraftPayloadValidator validator, Guid documentId, RecordPayload payload)
        => validator.ValidateUpdateDraftPayloadAsync(documentId, payload, EmptyParts, default);

    private static Task AssertInvalid(Func<Task> action) => action.Should().ThrowAsync<Exception>();

    private static DocumentRecord Document(
        Guid id,
        string typeCode,
        DocumentStatus status = DocumentStatus.Posted)
        => new()
        {
            Id = id,
            TypeCode = typeCode,
            DateUtc = DateTime.UnixEpoch,
            Status = status
        };

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> EmptyParts
        = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>(StringComparer.Ordinal);
}
