using FluentAssertions;
using Moq;
using System.Reflection;
using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Documents.Validation;
using NGB.PropertyManagement.Runtime.Exceptions;
using Xunit;

namespace NGB.PropertyManagement.Runtime.Tests.Documents.Validation;

public sealed class ApplyPostValidatorsFullCoverageTests
{
    [Fact]
    public async Task Payable_apply_validator_covers_all_document_states_context_mismatch_and_success()
    {
        var documentId = Guid.CreateVersion7();
        var creditId = Guid.CreateVersion7();
        var chargeId = Guid.CreateVersion7();
        var partyId = Guid.CreateVersion7();
        var propertyId = Guid.CreateVersion7();
        var readers = new Mock<IPropertyManagementDocumentReaders>(MockBehavior.Strict);
        var applySequence = readers.SetupSequence(x => x.ReadPayableApplyHeadAsync(documentId, It.IsAny<CancellationToken>()));
        (decimal Amount, Guid Credit, Guid Charge)[] cases =
        {
            (Amount: 0m, Credit: creditId, Charge: chargeId),
            (10m, creditId, creditId),
            (10m, creditId, chargeId), (10m, creditId, chargeId), (10m, creditId, chargeId),
            (10m, creditId, chargeId), (10m, creditId, chargeId), (10m, creditId, chargeId),
            (10m, creditId, chargeId), (10m, creditId, chargeId)
        };
        foreach (var item in cases)
        {
            applySequence.ReturnsAsync(new PmPayableApplyHead(
                documentId, item.Credit, item.Charge, DateOnly.MinValue, item.Amount, null));
        }

        var documents = new Mock<IDocumentRepository>(MockBehavior.Strict);
        documents.SetupSequence(x => x.GetAsync(creditId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentRecord?)null)
            .ReturnsAsync(Document(creditId, "wrong", DocumentStatus.Posted))
            .ReturnsAsync(Document(creditId, PropertyManagementCodes.PayablePayment, DocumentStatus.Draft))
            .ReturnsAsync(Document(creditId, PropertyManagementCodes.PayablePayment, DocumentStatus.Posted))
            .ReturnsAsync(Document(creditId, PropertyManagementCodes.PayablePayment, DocumentStatus.Posted))
            .ReturnsAsync(Document(creditId, PropertyManagementCodes.PayablePayment, DocumentStatus.Posted))
            .ReturnsAsync(Document(creditId, PropertyManagementCodes.PayablePayment, DocumentStatus.Posted))
            .ReturnsAsync(Document(creditId, PropertyManagementCodes.PayablePayment, DocumentStatus.Posted));
        documents.SetupSequence(x => x.GetAsync(chargeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentRecord?)null)
            .ReturnsAsync(Document(chargeId, "wrong", DocumentStatus.Posted))
            .ReturnsAsync(Document(chargeId, PropertyManagementCodes.PayableCharge, DocumentStatus.Draft))
            .ReturnsAsync(Document(chargeId, PropertyManagementCodes.PayableCharge, DocumentStatus.Posted))
            .ReturnsAsync(Document(chargeId, PropertyManagementCodes.PayableCharge, DocumentStatus.Posted));
        readers.Setup(x => x.ReadPayablePaymentHeadAsync(creditId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PmPayablePaymentHead(
                creditId, partyId, propertyId, null, DateOnly.MinValue, 10m, null));
        readers.SetupSequence(x => x.ReadPayableChargeHeadAsync(chargeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PmPayableChargeHead(
                chargeId, Guid.CreateVersion7(), propertyId, Guid.CreateVersion7(), DateOnly.MinValue, 10m, null, null))
            .ReturnsAsync(new PmPayableChargeHead(
                chargeId, partyId, propertyId, Guid.CreateVersion7(), DateOnly.MinValue, 10m, null, null));
        var validator = new PayableApplyPostValidator(readers.Object, documents.Object);

        validator.TypeCode.Should().Be(PropertyManagementCodes.PayableApply);
        for (var i = 0; i < 9; i++)
            await AssertThrows<PayableApplyValidationException>(Validate);
        await Validate();
        return;

        Task Validate() => validator.ValidateBeforePostAsync(Document(documentId, PropertyManagementCodes.PayableApply), default);
    }

    [Fact]
    public async Task Receivable_apply_validator_covers_all_document_states_every_charge_kind_mismatch_and_success()
    {
        var documentId = Guid.CreateVersion7();
        var creditId = Guid.CreateVersion7();
        var chargeId = Guid.CreateVersion7();
        var partyId = Guid.CreateVersion7();
        var propertyId = Guid.CreateVersion7();
        var leaseId = Guid.CreateVersion7();
        var readers = new Mock<IPropertyManagementDocumentReaders>(MockBehavior.Strict);
        var applySequence = readers.SetupSequence(x => x.ReadReceivableApplyHeadAsync(documentId, It.IsAny<CancellationToken>()));
        (decimal Amount, Guid Credit, Guid Charge)[] cases =
        {
            (Amount: 0m, Credit: creditId, Charge: chargeId),
            (10m, creditId, creditId),
            (10m, creditId, chargeId), (10m, creditId, chargeId), (10m, creditId, chargeId),
            (10m, creditId, chargeId), (10m, creditId, chargeId), (10m, creditId, chargeId),
            (10m, creditId, chargeId), (10m, creditId, chargeId), (10m, creditId, chargeId),
            (10m, creditId, chargeId)
        };
        foreach (var item in cases)
        {
            applySequence.ReturnsAsync(new PmReceivableApplyHead(
                documentId, item.Credit, item.Charge, DateOnly.MinValue, item.Amount, null));
        }

        var documents = new Mock<IDocumentRepository>(MockBehavior.Strict);
        var paymentSequence = documents.SetupSequence(x => x.GetAsync(creditId, It.IsAny<CancellationToken>()));
        paymentSequence.ReturnsAsync((DocumentRecord?)null)
            .ReturnsAsync(Document(creditId, "wrong", DocumentStatus.Posted))
            .ReturnsAsync(Document(creditId, PropertyManagementCodes.ReceivablePayment, DocumentStatus.Draft));
        for (var i = 0; i < 7; i++)
            paymentSequence.ReturnsAsync(Document(creditId, PropertyManagementCodes.ReceivablePayment, DocumentStatus.Posted));

        documents.SetupSequence(x => x.GetAsync(chargeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentRecord?)null)
            .ReturnsAsync(Document(chargeId, "wrong", DocumentStatus.Posted))
            .ReturnsAsync(Document(chargeId, PropertyManagementCodes.ReceivableCharge, DocumentStatus.Draft))
            .ReturnsAsync(Document(chargeId, PropertyManagementCodes.ReceivableCharge, DocumentStatus.Posted))
            .ReturnsAsync(Document(chargeId, PropertyManagementCodes.ReceivableCharge, DocumentStatus.Posted))
            .ReturnsAsync(Document(chargeId, PropertyManagementCodes.LateFeeCharge, DocumentStatus.Posted))
            .ReturnsAsync(Document(chargeId, PropertyManagementCodes.RentCharge, DocumentStatus.Posted));
        readers.Setup(x => x.ReadReceivablePaymentHeadAsync(creditId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PmReceivablePaymentHead(
                creditId, partyId, propertyId, leaseId, null, DateOnly.MinValue, 10m, null));
        readers.SetupSequence(x => x.ReadReceivableChargeHeadAsync(chargeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PmReceivableChargeHead(
                chargeId, Guid.CreateVersion7(), propertyId, leaseId, Guid.CreateVersion7(), DateOnly.MinValue, 10m, null))
            .ReturnsAsync(new PmReceivableChargeHead(
                chargeId, partyId, propertyId, leaseId, Guid.CreateVersion7(), DateOnly.MinValue, 10m, null));
        readers.Setup(x => x.ReadLateFeeChargeHeadAsync(chargeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PmLateFeeChargeHead(
                chargeId, partyId, propertyId, leaseId, DateOnly.MinValue, 10m, null));
        readers.Setup(x => x.ReadRentChargeHeadAsync(chargeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PmRentChargeHead(
                chargeId, leaseId, partyId, propertyId, DateOnly.MinValue, DateOnly.MinValue, DateOnly.MinValue, 10m, null));
        var validator = new ReceivableApplyPostValidator(readers.Object, documents.Object);

        validator.TypeCode.Should().Be(PropertyManagementCodes.ReceivableApply);
        for (var i = 0; i < 9; i++)
            await AssertThrows<ReceivableApplyValidationException>(Validate);
        await Validate();
        await Validate();
        await Validate();
        var readChargeLike = typeof(ReceivableApplyPostValidator).GetMethod(
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

        Task Validate() => validator.ValidateBeforePostAsync(Document(documentId, PropertyManagementCodes.ReceivableApply), default);
    }

    private static Task AssertThrows<T>(Func<Task> action) where T : Exception
        => action.Should().ThrowAsync<T>();

    private static DocumentRecord Document(Guid id, string typeCode, DocumentStatus status = DocumentStatus.Draft)
        => new()
        {
            Id = id,
            TypeCode = typeCode,
            DateUtc = DateTime.UnixEpoch,
            Status = status
        };
}
