using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Exceptions;

namespace NGB.PropertyManagement.Runtime.Receivables;

internal static class ReceivableCreditSourceResolver
{
    public static bool IsCreditSourceDocumentType(string? typeCode)
        => string.Equals(typeCode, PropertyManagementCodes.ReceivablePayment, StringComparison.OrdinalIgnoreCase)
           || string.Equals(typeCode, PropertyManagementCodes.ReceivableCreditMemo, StringComparison.OrdinalIgnoreCase);

    public static async Task<ReceivableCreditSourceContext> ReadRequiredAsync(
        IPropertyManagementDocumentReaders readers,
        IDocumentRepository documents,
        Guid creditDocumentId,
        CancellationToken ct)
    {
        var document = await documents.GetAsync(creditDocumentId, ct);
        if (document is null)
            throw ReceivableApplyValidationException.PaymentNotFound(creditDocumentId);

        return await ReadRequiredAsync(readers, document, ct);
    }

    public static async Task<ReceivableCreditSourceContext> ReadRequiredAsync(
        IPropertyManagementDocumentReaders readers,
        DocumentRecord document,
        CancellationToken ct)
    {
        if (string.Equals(document.TypeCode, PropertyManagementCodes.ReceivablePayment, StringComparison.OrdinalIgnoreCase))
        {
            var payment = await readers.ReadReceivablePaymentHeadAsync(document.Id, ct);
            return new ReceivableCreditSourceContext(
                DocumentId: payment.DocumentId,
                TypeCode: PropertyManagementCodes.ReceivablePayment,
                PartyId: payment.PartyId,
                PropertyId: payment.PropertyId,
                LeaseId: payment.LeaseId,
                CreditDateUtc: payment.ReceivedOnUtc,
                OriginalAmount: payment.Amount,
                Memo: payment.Memo);
        }

        if (string.Equals(document.TypeCode, PropertyManagementCodes.ReceivableCreditMemo, StringComparison.OrdinalIgnoreCase))
        {
            var creditMemo = await readers.ReadReceivableCreditMemoHeadAsync(document.Id, ct);
            return new ReceivableCreditSourceContext(
                DocumentId: creditMemo.DocumentId,
                TypeCode: PropertyManagementCodes.ReceivableCreditMemo,
                PartyId: creditMemo.PartyId,
                PropertyId: creditMemo.PropertyId,
                LeaseId: creditMemo.LeaseId,
                CreditDateUtc: creditMemo.CreditedOnUtc,
                OriginalAmount: creditMemo.Amount,
                Memo: creditMemo.Memo);
        }

        throw ReceivableApplyValidationException.PaymentWrongType(document.Id, document.TypeCode);
    }
}

internal sealed record ReceivableCreditSourceContext(
    Guid DocumentId,
    string TypeCode,
    Guid PartyId,
    Guid PropertyId,
    Guid LeaseId,
    DateOnly CreditDateUtc,
    decimal OriginalAmount,
    string? Memo);
