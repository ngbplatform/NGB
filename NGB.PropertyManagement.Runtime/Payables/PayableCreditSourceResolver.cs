using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Exceptions;

namespace NGB.PropertyManagement.Runtime.Payables;

internal static class PayableCreditSourceResolver
{
    public static bool IsCreditSourceDocumentType(string? typeCode)
        => string.Equals(typeCode, PropertyManagementCodes.PayablePayment, StringComparison.OrdinalIgnoreCase)
           || string.Equals(typeCode, PropertyManagementCodes.PayableCreditMemo, StringComparison.OrdinalIgnoreCase);

    public static async Task<PayableCreditSourceContext> ReadRequiredAsync(
        IPropertyManagementDocumentReaders readers,
        IDocumentRepository documents,
        Guid creditDocumentId,
        CancellationToken ct)
    {
        var document = await documents.GetAsync(creditDocumentId, ct);
        if (document is null)
            throw PayableApplyValidationException.CreditSourceNotFound(creditDocumentId);

        return await ReadRequiredAsync(readers, document, ct);
    }

    public static async Task<PayableCreditSourceContext> ReadRequiredAsync(
        IPropertyManagementDocumentReaders readers,
        DocumentRecord document,
        CancellationToken ct)
    {
        if (string.Equals(document.TypeCode, PropertyManagementCodes.PayablePayment, StringComparison.OrdinalIgnoreCase))
        {
            var payment = await readers.ReadPayablePaymentHeadAsync(document.Id, ct);
            return new PayableCreditSourceContext(
                DocumentId: payment.DocumentId,
                TypeCode: PropertyManagementCodes.PayablePayment,
                PartyId: payment.PartyId,
                PropertyId: payment.PropertyId,
                CreditDateUtc: payment.PaidOnUtc,
                OriginalAmount: payment.Amount,
                Memo: payment.Memo);
        }

        if (string.Equals(document.TypeCode, PropertyManagementCodes.PayableCreditMemo, StringComparison.OrdinalIgnoreCase))
        {
            var creditMemo = await readers.ReadPayableCreditMemoHeadAsync(document.Id, ct);
            return new PayableCreditSourceContext(
                DocumentId: creditMemo.DocumentId,
                TypeCode: PropertyManagementCodes.PayableCreditMemo,
                PartyId: creditMemo.PartyId,
                PropertyId: creditMemo.PropertyId,
                CreditDateUtc: creditMemo.CreditedOnUtc,
                OriginalAmount: creditMemo.Amount,
                Memo: creditMemo.Memo);
        }

        throw PayableApplyValidationException.CreditSourceWrongType(document.Id, document.TypeCode);
    }
}

internal sealed record PayableCreditSourceContext(
    Guid DocumentId,
    string TypeCode,
    Guid PartyId,
    Guid PropertyId,
    DateOnly CreditDateUtc,
    decimal OriginalAmount,
    string? Memo);
