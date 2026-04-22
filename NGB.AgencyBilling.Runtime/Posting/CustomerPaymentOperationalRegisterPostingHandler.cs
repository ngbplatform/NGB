using NGB.AgencyBilling.Documents;
using NGB.AgencyBilling.Runtime.Policy;
using NGB.Core.Documents;
using NGB.Definitions.Documents.Posting;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.OperationalRegisters;
using NGB.Runtime.Dimensions;
using NGB.Tools.Exceptions;

namespace NGB.AgencyBilling.Runtime.Posting;

public sealed class CustomerPaymentOperationalRegisterPostingHandler(
    IAgencyBillingDocumentReaders readers,
    IAgencyBillingAccountingPolicyReader policyReader,
    IOperationalRegisterRepository registers,
    IDimensionSetService dimensionSets)
    : IDocumentOperationalRegisterPostingHandler
{
    public string TypeCode => AgencyBillingCodes.CustomerPayment;

    public async Task BuildMovementsAsync(
        DocumentRecord document,
        IOperationalRegisterMovementsBuilder builder,
        CancellationToken ct)
    {
        var payment = await readers.ReadCustomerPaymentHeadAsync(document.Id, ct);
        var applies = await readers.ReadCustomerPaymentAppliesAsync(document.Id, ct);
        var policy = await policyReader.GetRequiredAsync(ct);
        var projectBillingStatusRegister = await registers.GetByIdAsync(policy.ProjectBillingStatusOperationalRegisterId, ct);
        var arOpenItemsRegister = await registers.GetByIdAsync(policy.ArOpenItemsOperationalRegisterId, ct);

        if (projectBillingStatusRegister is null)
            throw new NgbConfigurationViolationException($"Operational register '{policy.ProjectBillingStatusOperationalRegisterId}' was not found.");

        if (arOpenItemsRegister is null)
            throw new NgbConfigurationViolationException($"Operational register '{policy.ArOpenItemsOperationalRegisterId}' was not found.");

        var occurredAtUtc = AgencyBillingPostingCommon.ToOccurredAtUtc(payment.DocumentDateUtc);
        var invoiceHeads = new Dictionary<Guid, AgencyBillingSalesInvoiceHead>();

        foreach (var apply in applies)
        {
            if (!invoiceHeads.TryGetValue(apply.SalesInvoiceId, out var invoice))
            {
                invoice = await readers.ReadSalesInvoiceHeadAsync(apply.SalesInvoiceId, ct);
                invoiceHeads[apply.SalesInvoiceId] = invoice;
            }

            var amount = AgencyBillingPostingCommon.RoundScale4(apply.AppliedAmount);
            if (amount <= 0m)
                continue;

            var dimensionSetId = await dimensionSets.GetOrCreateIdAsync(
                AgencyBillingPostingCommon.ProjectBag(invoice.ClientId, invoice.ProjectId),
                ct);
            var arOpenItemDimensionSetId = await dimensionSets.GetOrCreateIdAsync(
                AgencyBillingPostingCommon.ArOpenItemBag(invoice.ClientId, invoice.ProjectId, invoice.DocumentId),
                ct);

            builder.Add(
                projectBillingStatusRegister.Code,
                new OperationalRegisterMovement(
                    DocumentId: document.Id,
                    OccurredAtUtc: occurredAtUtc,
                    DimensionSetId: dimensionSetId,
                    Resources: AgencyBillingPostingCommon.BuildProjectBillingStatusResources(
                        billedAmountDelta: 0m,
                        collectedAmountDelta: amount,
                        outstandingArAmountDelta: -amount)));

            builder.Add(
                arOpenItemsRegister.Code,
                new OperationalRegisterMovement(
                    DocumentId: document.Id,
                    OccurredAtUtc: occurredAtUtc,
                    DimensionSetId: arOpenItemDimensionSetId,
                    Resources: new Dictionary<string, decimal> { ["amount"] = -amount }));
        }
    }
}
