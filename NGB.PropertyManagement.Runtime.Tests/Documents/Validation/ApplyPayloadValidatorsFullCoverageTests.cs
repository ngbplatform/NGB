using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Moq;
using NGB.Contracts.Common;
using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Documents.Validation;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.Runtime.Documents.Validation;
using Xunit;

namespace NGB.PropertyManagement.Runtime.Tests.Documents.Validation;

public sealed class ApplyPayloadValidatorsFullCoverageTests
{
    [Fact]
    public async Task Payable_apply_payload_covers_all_reference_rules_mismatch_success_and_update_fallbacks()
    {
        var documentId = Guid.CreateVersion7();
        var creditId = Guid.CreateVersion7();
        var chargeId = Guid.CreateVersion7();
        var partyId = Guid.CreateVersion7();
        var propertyId = Guid.CreateVersion7();
        var readers = new Mock<IPropertyManagementDocumentReaders>(MockBehavior.Strict);
        readers.Setup(x => x.ReadPayableApplyHeadAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PmPayableApplyHead(documentId, creditId, chargeId, AppliedOn, 10m, null));
        var documents = new Mock<IDocumentRepository>(MockBehavior.Strict);
        var credits = documents.SetupSequence(x => x.GetAsync(creditId, It.IsAny<CancellationToken>()));
        credits.ReturnsAsync((DocumentRecord?)null)
            .ReturnsAsync(Document(creditId, "wrong"));
        for (var i = 0; i < 6; i++)
            credits.ReturnsAsync(Document(creditId, PropertyManagementCodes.PayablePayment));
        documents.SetupSequence(x => x.GetAsync(chargeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentRecord?)null)
            .ReturnsAsync(Document(chargeId, "wrong"))
            .ReturnsAsync(Document(chargeId, PropertyManagementCodes.PayableCharge))
            .ReturnsAsync(Document(chargeId, PropertyManagementCodes.PayableCharge))
            .ReturnsAsync(Document(chargeId, PropertyManagementCodes.PayableCharge))
            .ReturnsAsync(Document(chargeId, PropertyManagementCodes.PayableCharge));
        readers.Setup(x => x.ReadPayablePaymentHeadAsync(creditId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PmPayablePaymentHead(creditId, partyId, propertyId, null, AppliedOn, 10m, null));
        readers.SetupSequence(x => x.ReadPayableChargeHeadAsync(chargeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PmPayableChargeHead(
                chargeId, Guid.CreateVersion7(), propertyId, Guid.CreateVersion7(), AppliedOn, 10m, null, null))
            .ReturnsAsync(Charge())
            .ReturnsAsync(Charge())
            .ReturnsAsync(Charge());
        var validator = new PayableApplyPayloadValidator(readers.Object, documents.Object);

        validator.TypeCode.Should().Be(PropertyManagementCodes.PayableApply);
        await AssertInvalid<PayableApplyValidationException>(() => Create(validator, Full(0m)));
        await AssertInvalid<PayableApplyValidationException>(() => Create(validator, Full(10m, sameDocument: true)));
        for (var i = 0; i < 5; i++)
            await AssertInvalid<PayableApplyValidationException>(() => Create(validator, Full(10m)));
        await Create(validator, Full(10m));
        await Update(validator, documentId, Payload(("amount", 20m)));
        await Update(validator, documentId, Payload(("credit_document_id", creditId)));
        return;

        PmPayableChargeHead Charge() => new(
            chargeId, partyId, propertyId, Guid.CreateVersion7(), AppliedOn, 10m, null, null);
        RecordPayload Full(decimal amount, bool sameDocument = false) => Payload(
            ("credit_document_id", creditId),
            ("charge_document_id", sameDocument ? creditId : chargeId),
            ("applied_on_utc", AppliedOn),
            ("amount", amount));
    }

    [Fact]
    public async Task Receivable_apply_payload_covers_all_references_charge_kinds_mismatch_and_update_fallbacks()
    {
        var documentId = Guid.CreateVersion7();
        var creditId = Guid.CreateVersion7();
        var chargeId = Guid.CreateVersion7();
        var partyId = Guid.CreateVersion7();
        var propertyId = Guid.CreateVersion7();
        var leaseId = Guid.CreateVersion7();
        var readers = new Mock<IPropertyManagementDocumentReaders>(MockBehavior.Strict);
        readers.Setup(x => x.ReadReceivableApplyHeadAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PmReceivableApplyHead(documentId, creditId, chargeId, AppliedOn, 10m, null));
        var documents = new Mock<IDocumentRepository>(MockBehavior.Strict);
        var credits = documents.SetupSequence(x => x.GetAsync(creditId, It.IsAny<CancellationToken>()));
        credits.ReturnsAsync((DocumentRecord?)null)
            .ReturnsAsync(Document(creditId, "wrong"));
        for (var i = 0; i < 8; i++)
            credits.ReturnsAsync(Document(creditId, PropertyManagementCodes.ReceivablePayment));
        documents.SetupSequence(x => x.GetAsync(chargeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentRecord?)null)
            .ReturnsAsync(Document(chargeId, "wrong"))
            .ReturnsAsync(Document(chargeId, PropertyManagementCodes.ReceivableCharge))
            .ReturnsAsync(Document(chargeId, PropertyManagementCodes.ReceivableCharge))
            .ReturnsAsync(Document(chargeId, PropertyManagementCodes.LateFeeCharge))
            .ReturnsAsync(Document(chargeId, PropertyManagementCodes.RentCharge))
            .ReturnsAsync(Document(chargeId, PropertyManagementCodes.ReceivableCharge))
            .ReturnsAsync(Document(chargeId, PropertyManagementCodes.ReceivableCharge));
        readers.Setup(x => x.ReadReceivablePaymentHeadAsync(creditId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PmReceivablePaymentHead(
                creditId, partyId, propertyId, leaseId, null, AppliedOn, 10m, null));
        readers.SetupSequence(x => x.ReadReceivableChargeHeadAsync(chargeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PmReceivableChargeHead(
                chargeId, Guid.CreateVersion7(), propertyId, leaseId, Guid.CreateVersion7(), AppliedOn, 10m, null))
            .ReturnsAsync(Charge())
            .ReturnsAsync(Charge())
            .ReturnsAsync(Charge());
        readers.Setup(x => x.ReadLateFeeChargeHeadAsync(chargeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PmLateFeeChargeHead(chargeId, partyId, propertyId, leaseId, AppliedOn, 10m, null));
        readers.Setup(x => x.ReadRentChargeHeadAsync(chargeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PmRentChargeHead(
                chargeId, leaseId, partyId, propertyId, AppliedOn, AppliedOn, AppliedOn, 10m, null));
        var validator = new ReceivableApplyPayloadValidator(readers.Object, documents.Object);

        validator.TypeCode.Should().Be(PropertyManagementCodes.ReceivableApply);
        await AssertInvalid<ReceivableApplyValidationException>(() => Create(validator, Full(0m)));
        await AssertInvalid<ReceivableApplyValidationException>(() => Create(validator, Full(10m, sameDocument: true)));
        for (var i = 0; i < 5; i++)
            await AssertInvalid<ReceivableApplyValidationException>(() => Create(validator, Full(10m)));
        await Create(validator, Full(10m));
        await Create(validator, Full(10m));
        await Create(validator, Full(10m));
        await Update(validator, documentId, Payload(("amount", 20m)));
        await Update(validator, documentId, Payload(("applied_on_utc", AppliedOn)));

        var readChargeLike = typeof(ReceivableApplyPayloadValidator).GetMethod(
            "ReadChargeLikeContextAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        await ((Func<Task>)(async () =>
            {
                var invocation = (Task)readChargeLike.Invoke(
                    validator,
                    [chargeId, "unsupported", CancellationToken.None])!;
                await invocation;
            }))
            .Should().ThrowAsync<ReceivableApplyValidationException>();
        return;

        PmReceivableChargeHead Charge() => new(
            chargeId, partyId, propertyId, leaseId, Guid.CreateVersion7(), AppliedOn, 10m, null);
        RecordPayload Full(decimal amount, bool sameDocument = false) => Payload(
            ("credit_document_id", creditId),
            ("charge_document_id", sameDocument ? creditId : chargeId),
            ("applied_on_utc", AppliedOn),
            ("amount", amount));
    }

    private static readonly DateOnly AppliedOn = new(2026, 8, 16);

    private static RecordPayload Payload(params (string Key, object? Value)[] values)
        => new(values.ToDictionary(
            pair => pair.Key,
            pair => JsonSerializer.SerializeToElement(pair.Value),
            StringComparer.Ordinal));

    private static Task Create(IDocumentDraftPayloadValidator validator, RecordPayload payload)
        => validator.ValidateCreateDraftPayloadAsync(payload, EmptyParts, default);

    private static Task Update(IDocumentDraftPayloadValidator validator, Guid documentId, RecordPayload payload)
        => validator.ValidateUpdateDraftPayloadAsync(documentId, payload, EmptyParts, default);

    private static Task AssertInvalid<T>(Func<Task> action) where T : Exception
        => action.Should().ThrowAsync<T>();

    private static DocumentRecord Document(Guid id, string typeCode)
        => new()
        {
            Id = id,
            TypeCode = typeCode,
            DateUtc = DateTime.UnixEpoch,
            Status = DocumentStatus.Posted
        };

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> EmptyParts
        = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>(StringComparer.Ordinal);
}
