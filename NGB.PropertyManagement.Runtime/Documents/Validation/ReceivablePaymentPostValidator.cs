using NGB.Core.Documents;
using NGB.Definitions.Documents.Validation;
using NGB.Persistence.Documents;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.PropertyManagement.Runtime.Policy;

namespace NGB.PropertyManagement.Runtime.Documents.Validation;

public sealed class ReceivablePaymentPostValidator(
    IPropertyManagementDocumentReaders readers,
    IDocumentRepository documents,
    IPropertyManagementBankAccountReader bankAccounts,
    IPropertyManagementPartyReader parties)
    : IDocumentPostValidator
{
    public string TypeCode => PropertyManagementCodes.ReceivablePayment;

    public async Task ValidateBeforePostAsync(DocumentRecord documentForUpdate, CancellationToken ct)
    {
        DocumentValidatorBindingGuard.EnsureExpectedType(documentForUpdate, TypeCode, nameof(ReceivablePaymentPostValidator));

        var payment = await readers.ReadReceivablePaymentHeadAsync(documentForUpdate.Id, ct);
        var leaseDocument = await documents.GetAsync(payment.LeaseId, ct);
        if (leaseDocument is null
            || !string.Equals(leaseDocument.TypeCode, PropertyManagementCodes.Lease, StringComparison.OrdinalIgnoreCase))
        {
            throw ReceivablePaymentValidationException.LeaseNotFound(payment.LeaseId, documentForUpdate.Id);
        }

        if (leaseDocument.Status == DocumentStatus.MarkedForDeletion)
            throw ReceivablePaymentValidationException.LeaseMarkedForDeletion(payment.LeaseId, documentForUpdate.Id);

        if (payment.Amount <= 0m)
            throw ReceivablePaymentValidationException.AmountMustBePositive(payment.Amount, documentForUpdate.Id);

        var lease = await readers.ReadLeaseHeadAsync(payment.LeaseId, ct);
        await PartyRoleValidationGuards.EnsureTenantPartyAsync(TypeCode, "lease_id", lease.PrimaryPartyId, parties, ct);

        var property = await readers.ReadPropertyHeadAsync(payment.PropertyId, ct);
        if (property is null)
            throw new DocumentPropertyNotFoundException(TypeCode, payment.PropertyId);

        if (property.IsDeleted)
            throw new DocumentPropertyDeletedException(TypeCode, payment.PropertyId);

        if (!string.Equals(property.Kind, "Unit", StringComparison.OrdinalIgnoreCase))
            throw new DocumentPropertyMustBeUnitException(TypeCode, payment.PropertyId, property.Kind);

        if (payment.BankAccountId is { } bankAccountId)
        {
            var bankAccount = await bankAccounts.TryGetAsync(bankAccountId, ct);
            if (bankAccount is null)
                throw ReceivablePaymentValidationException.BankAccountNotFound(bankAccountId, documentForUpdate.Id);

            if (bankAccount.IsDeleted)
                throw ReceivablePaymentValidationException.BankAccountDeleted(bankAccountId, documentForUpdate.Id);
        }
    }
}
