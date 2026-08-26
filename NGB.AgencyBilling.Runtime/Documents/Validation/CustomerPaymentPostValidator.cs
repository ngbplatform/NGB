using NGB.AgencyBilling.Documents;
using NGB.AgencyBilling.References;
using NGB.AgencyBilling.Runtime.Posting;
using NGB.AgencyBilling.Runtime.Policy;
using NGB.AgencyBilling.Runtime.Validation;
using NGB.Accounting.Accounts;
using NGB.Core.Dimensions;
using NGB.Core.Documents;
using NGB.Definitions.Documents.Validation;
using NGB.Persistence.Documents;
using NGB.Persistence.Locks;
using NGB.Persistence.OperationalRegisters;
using NGB.Runtime.Locks;
using NGB.Tools.Exceptions;

namespace NGB.AgencyBilling.Runtime.Documents.Validation;

public sealed class CustomerPaymentPostValidator(
    IAgencyBillingDocumentReaders readers,
    IAgencyBillingReferenceReaders references,
    IDocumentRepository documents,
    IChartOfAccountsProvider charts,
    IAgencyBillingAccountingPolicyReader policyReader,
    IOperationalRegisterRepository registers,
    IOperationalRegisterResourceNetReader netReader,
    IAdvisoryLockManager locks)
    : IDocumentPostValidator
{
    public string TypeCode => AgencyBillingCodes.CustomerPayment;

    public async Task ValidateBeforePostAsync(DocumentRecord documentForUpdate, CancellationToken ct)
    {
        AgencyBillingDocumentValidatorBindingGuard.EnsureExpectedType(documentForUpdate, TypeCode, nameof(CustomerPaymentPostValidator));

        var payment = await readers.ReadCustomerPaymentHeadAsync(documentForUpdate.Id, ct);
        var applies = await readers.ReadCustomerPaymentAppliesAsync(documentForUpdate.Id, ct);

        await AgencyBillingCatalogValidationGuards.EnsureClientAsync(payment.ClientId, "client_id", references, ct, requireOperationallyActive: true);

        if (payment.Amount <= 0m)
            throw new NgbArgumentInvalidException("amount", "Amount must be greater than zero.");

        if (payment.CashAccountId is { } cashAccountId && cashAccountId != Guid.Empty)
            await AgencyBillingAccountingValidationGuards.EnsureCashAccountAsync(cashAccountId, "cash_account_id", charts, ct);

        if (applies.Count == 0)
            throw new NgbArgumentInvalidException("applies", "Customer Payment must contain at least one apply row.");

        var policy = await policyReader.GetRequiredAsync(ct);
        var arOpenItemsRegister = await registers.GetByIdAsync(policy.ArOpenItemsOperationalRegisterId, ct);
        if (arOpenItemsRegister is null)
            throw new NgbConfigurationViolationException($"Operational register '{policy.ArOpenItemsOperationalRegisterId}' was not found.");

        var invoiceIds = applies
            .Select(x => x.SalesInvoiceId)
            .Distinct()
            .OrderBy(x => x)
            .ToArray();

        await locks.LockDocumentsDeterministicallyAsync(invoiceIds, ct);

        var invoiceDocuments = await documents.GetByIdsAsync(invoiceIds, ct);
        var invoiceHeads = await readers.ReadSalesInvoiceHeadsAsync(invoiceIds, ct);
        var dimensionSetByInvoiceId = invoiceHeads.ToDictionary(
            static pair => pair.Key,
            static pair => DeterministicDimensionSetId.FromBag(
                AgencyBillingPostingCommon.ArOpenItemBag(
                    pair.Value.ClientId,
                    pair.Value.ProjectId,
                    pair.Value.DocumentId)));
        var openAmounts = await netReader.GetNetByDimensionSetsAsync(
            arOpenItemsRegister.RegisterId,
            dimensionSetByInvoiceId.Values.ToArray(),
            resourceColumnCode: "amount",
            ct);

        var totalApplied = 0m;
        var groupedApplies = applies
            .GroupBy(x => x.SalesInvoiceId)
            .ToDictionary(x => x.Key, x => x.ToArray());

        foreach (var (invoiceId, invoiceApplies) in groupedApplies)
        {
            if (!invoiceDocuments.TryGetValue(invoiceId, out var invoiceDocument)
                || !string.Equals(invoiceDocument.TypeCode, AgencyBillingCodes.SalesInvoice, StringComparison.OrdinalIgnoreCase))
            {
                throw new NgbArgumentInvalidException("applies", "Referenced sales invoice was not found.");
            }

            if (invoiceDocument.Status != DocumentStatus.Posted)
                throw new NgbArgumentInvalidException("applies", "Referenced sales invoice must be posted.");

            if (!invoiceHeads.TryGetValue(invoiceId, out var invoice))
                throw new NgbConfigurationViolationException($"Sales Invoice document '{invoiceId}' is missing its Agency Billing head row.");

            if (invoice.ClientId != payment.ClientId)
                throw new NgbArgumentInvalidException("applies", "Payment client must match the client on every applied sales invoice.");

            var invoiceAppliedAmount = 0m;
            foreach (var apply in invoiceApplies)
            {
                if (apply.AppliedAmount <= 0m)
                    throw new NgbArgumentInvalidException("applies", "Applied Amount must be greater than zero.");

                invoiceAppliedAmount = AgencyBillingPostingCommon.RoundScale4(invoiceAppliedAmount + apply.AppliedAmount);
            }

            var dimensionSetId = dimensionSetByInvoiceId[invoiceId];
            openAmounts.TryGetValue(dimensionSetId, out var openAmount);

            if (invoiceAppliedAmount > AgencyBillingPostingCommon.RoundScale4(openAmount))
            {
                throw new NgbArgumentInvalidException(
                    "applies",
                    $"Applied amount exceeds the remaining open amount for sales invoice '{invoice.DocumentId}'.");
            }

            totalApplied = AgencyBillingPostingCommon.RoundScale4(totalApplied + invoiceAppliedAmount);
        }

        if (AgencyBillingPostingCommon.RoundScale4(payment.Amount) != totalApplied)
            throw new NgbArgumentInvalidException("amount", "Payment Amount must equal the sum of Applied Amount values.");
    }
}
