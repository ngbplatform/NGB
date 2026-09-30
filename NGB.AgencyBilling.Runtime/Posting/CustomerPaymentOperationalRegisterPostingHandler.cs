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
        var invoiceHeads = await readers.ReadSalesInvoiceHeadsAsync(
            applies.Select(static apply => apply.SalesInvoiceId).Distinct().ToArray(),
            ct);
        var movementDrafts = new List<(decimal Amount, NGB.Core.Dimensions.DimensionBag ProjectBag, NGB.Core.Dimensions.DimensionBag OpenItemBag)>();

        foreach (var apply in applies)
        {
            if (!invoiceHeads.TryGetValue(apply.SalesInvoiceId, out var invoice))
                throw new NgbInvariantViolationException($"Sales Invoice '{apply.SalesInvoiceId}' is missing its Agency Billing head row.");

            var amount = AgencyBillingPostingCommon.RoundScale4(apply.AppliedAmount);
            if (amount <= 0m)
                continue;

            movementDrafts.Add((
                amount,
                AgencyBillingPostingCommon.ProjectBag(invoice.ClientId, invoice.ProjectId),
                AgencyBillingPostingCommon.ArOpenItemBag(invoice.ClientId, invoice.ProjectId, invoice.DocumentId)));
        }

        var bags = movementDrafts
            .SelectMany(static x => new[] { x.ProjectBag, x.OpenItemBag })
            .ToArray();
        var dimensionSetIds = await dimensionSets.GetOrCreateIdsAsync(bags, ct);

        for (var i = 0; i < movementDrafts.Count; i++)
        {
            var draft = movementDrafts[i];
            var dimensionSetId = dimensionSetIds[i * 2];
            var arOpenItemDimensionSetId = dimensionSetIds[(i * 2) + 1];

            builder.Add(
                projectBillingStatusRegister.Code,
                new OperationalRegisterMovement(
                    DocumentId: document.Id,
                    OccurredAtUtc: occurredAtUtc,
                    DimensionSetId: dimensionSetId,
                    Resources: AgencyBillingPostingCommon.BuildProjectBillingStatusResources(
                        billedAmountDelta: 0m,
                        collectedAmountDelta: draft.Amount,
                        outstandingArAmountDelta: -draft.Amount)));

            builder.Add(
                arOpenItemsRegister.Code,
                new OperationalRegisterMovement(
                    DocumentId: document.Id,
                    OccurredAtUtc: occurredAtUtc,
                    DimensionSetId: arOpenItemDimensionSetId,
                    Resources: new Dictionary<string, decimal> { ["amount"] = -draft.Amount }));
        }
    }
}
