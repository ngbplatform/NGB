using NGB.Core.Documents;
using NGB.Definitions.Documents.Validation;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.PropertyManagement.Runtime.Policy;

namespace NGB.PropertyManagement.Runtime.Documents.Validation;

public sealed class PayablePaymentPostValidator(
    IPropertyManagementDocumentReaders readers,
    IPropertyManagementBankAccountReader bankAccounts,
    IPropertyManagementPartyReader parties)
    : IDocumentPostValidator
{
    public string TypeCode => PropertyManagementCodes.PayablePayment;

    public async Task ValidateBeforePostAsync(DocumentRecord documentForUpdate, CancellationToken ct)
    {
        DocumentValidatorBindingGuard.EnsureExpectedType(documentForUpdate, TypeCode, nameof(PayablePaymentPostValidator));

        var payment = await readers.ReadPayablePaymentHeadAsync(documentForUpdate.Id, ct);

        var party = await parties.TryGetAsync(payment.PartyId, ct);
        if (party is null)
            throw PayablePaymentValidationException.VendorNotFound(payment.PartyId, documentForUpdate.Id);

        if (party.IsDeleted)
            throw PayablePaymentValidationException.VendorDeleted(payment.PartyId, documentForUpdate.Id);

        if (!party.IsVendor)
            throw PayablePaymentValidationException.VendorRoleRequired(payment.PartyId, documentForUpdate.Id);

        if (payment.Amount <= 0m)
            throw PayablePaymentValidationException.AmountMustBePositive(payment.Amount, documentForUpdate.Id);

        var property = await readers.ReadPropertyHeadAsync(payment.PropertyId, ct);
        if (property is null)
            throw PayablePaymentValidationException.PropertyNotFound(payment.PropertyId, documentForUpdate.Id);

        if (property.IsDeleted)
            throw PayablePaymentValidationException.PropertyDeleted(payment.PropertyId, documentForUpdate.Id);

        if (payment.BankAccountId is { } bankAccountId)
        {
            var bankAccount = await bankAccounts.TryGetAsync(bankAccountId, ct);
            if (bankAccount is null)
                throw PayablePaymentValidationException.BankAccountNotFound(bankAccountId, documentForUpdate.Id);

            if (bankAccount.IsDeleted)
                throw PayablePaymentValidationException.BankAccountDeleted(bankAccountId, documentForUpdate.Id);
        }
    }
}
