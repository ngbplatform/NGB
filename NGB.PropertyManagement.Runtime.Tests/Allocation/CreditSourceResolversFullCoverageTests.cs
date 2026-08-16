using FluentAssertions;
using Moq;
using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.PropertyManagement.Runtime.Payables;
using NGB.PropertyManagement.Runtime.Receivables;
using Xunit;

namespace NGB.PropertyManagement.Runtime.Tests.Allocation;

public sealed class CreditSourceResolversFullCoverageTests
{
    private static readonly DateOnly Day = new(2026, 8, 16);

    [Theory]
    [InlineData(null, false)]
    [InlineData("wrong", false)]
    [InlineData("PM.PAYABLE_PAYMENT", true)]
    [InlineData("pm.payable_credit_memo", true)]
    public void Payable_credit_source_type_detection_is_case_insensitive(string? typeCode, bool expected)
        => PayableCreditSourceResolver.IsCreditSourceDocumentType(typeCode).Should().Be(expected);

    [Fact]
    public async Task Payable_credit_source_resolver_covers_missing_payment_credit_memo_and_wrong_type()
    {
        var paymentId = Guid.CreateVersion7();
        var memoId = Guid.CreateVersion7();
        var partyId = Guid.CreateVersion7();
        var propertyId = Guid.CreateVersion7();
        var readers = new Mock<IPropertyManagementDocumentReaders>(MockBehavior.Strict);
        readers.Setup(x => x.ReadPayablePaymentHeadAsync(paymentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PmPayablePaymentHead(paymentId, partyId, propertyId, null, Day, 10m, "payment"));
        readers.Setup(x => x.ReadPayableCreditMemoHeadAsync(memoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PmPayableCreditMemoHead(memoId, partyId, propertyId, Guid.CreateVersion7(), Day, 20m, "memo"));

        var documents = new Mock<IDocumentRepository>(MockBehavior.Strict);
        var missingId = Guid.CreateVersion7();
        documents.Setup(x => x.GetAsync(missingId, It.IsAny<CancellationToken>())).ReturnsAsync((DocumentRecord?)null);
        await ((Func<Task>)(() => PayableCreditSourceResolver.ReadRequiredAsync(readers.Object, documents.Object, missingId, default)))
            .Should().ThrowAsync<PayableApplyValidationException>();

        var payment = await PayableCreditSourceResolver.ReadRequiredAsync(
            readers.Object,
            Document(paymentId, PropertyManagementCodes.PayablePayment.ToUpperInvariant()),
            default);
        payment.Should().Be(new PayableCreditSourceContext(
            paymentId, PropertyManagementCodes.PayablePayment, partyId, propertyId, Day, 10m, "payment"));

        documents.Setup(x => x.GetAsync(memoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Document(memoId, PropertyManagementCodes.PayableCreditMemo));
        var memo = await PayableCreditSourceResolver.ReadRequiredAsync(readers.Object, documents.Object, memoId, default);
        memo.Should().Be(new PayableCreditSourceContext(
            memoId, PropertyManagementCodes.PayableCreditMemo, partyId, propertyId, Day, 20m, "memo"));

        await ((Func<Task>)(() => PayableCreditSourceResolver.ReadRequiredAsync(
                readers.Object, Document(Guid.CreateVersion7(), "wrong"), default)))
            .Should().ThrowAsync<PayableApplyValidationException>();
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("wrong", false)]
    [InlineData("PM.RECEIVABLE_PAYMENT", true)]
    [InlineData("pm.receivable_credit_memo", true)]
    public void Receivable_credit_source_type_detection_is_case_insensitive(string? typeCode, bool expected)
        => ReceivableCreditSourceResolver.IsCreditSourceDocumentType(typeCode).Should().Be(expected);

    [Fact]
    public async Task Receivable_credit_source_resolver_covers_missing_payment_credit_memo_and_wrong_type()
    {
        var paymentId = Guid.CreateVersion7();
        var memoId = Guid.CreateVersion7();
        var partyId = Guid.CreateVersion7();
        var propertyId = Guid.CreateVersion7();
        var leaseId = Guid.CreateVersion7();
        var readers = new Mock<IPropertyManagementDocumentReaders>(MockBehavior.Strict);
        readers.Setup(x => x.ReadReceivablePaymentHeadAsync(paymentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PmReceivablePaymentHead(paymentId, partyId, propertyId, leaseId, null, Day, 10m, "payment"));
        readers.Setup(x => x.ReadReceivableCreditMemoHeadAsync(memoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PmReceivableCreditMemoHead(memoId, partyId, propertyId, leaseId, null, Day, 20m, "memo"));

        var documents = new Mock<IDocumentRepository>(MockBehavior.Strict);
        var missingId = Guid.CreateVersion7();
        documents.Setup(x => x.GetAsync(missingId, It.IsAny<CancellationToken>())).ReturnsAsync((DocumentRecord?)null);
        await ((Func<Task>)(() => ReceivableCreditSourceResolver.ReadRequiredAsync(readers.Object, documents.Object, missingId, default)))
            .Should().ThrowAsync<ReceivableApplyValidationException>();

        var payment = await ReceivableCreditSourceResolver.ReadRequiredAsync(
            readers.Object,
            Document(paymentId, PropertyManagementCodes.ReceivablePayment.ToUpperInvariant()),
            default);
        payment.Should().Be(new ReceivableCreditSourceContext(
            paymentId, PropertyManagementCodes.ReceivablePayment, partyId, propertyId, leaseId, Day, 10m, "payment"));

        documents.Setup(x => x.GetAsync(memoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Document(memoId, PropertyManagementCodes.ReceivableCreditMemo));
        var memo = await ReceivableCreditSourceResolver.ReadRequiredAsync(readers.Object, documents.Object, memoId, default);
        memo.Should().Be(new ReceivableCreditSourceContext(
            memoId, PropertyManagementCodes.ReceivableCreditMemo, partyId, propertyId, leaseId, Day, 20m, "memo"));

        await ((Func<Task>)(() => ReceivableCreditSourceResolver.ReadRequiredAsync(
                readers.Object, Document(Guid.CreateVersion7(), "wrong"), default)))
            .Should().ThrowAsync<ReceivableApplyValidationException>();
    }

    private static DocumentRecord Document(Guid id, string typeCode)
        => new()
        {
            Id = id,
            TypeCode = typeCode,
            DateUtc = DateTime.UnixEpoch,
            Status = DocumentStatus.Posted
        };
}
