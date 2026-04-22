using NGB.AgencyBilling.Documents;
using NGB.AgencyBilling.Runtime.Policy;
using NGB.Core.Documents;
using NGB.Definitions.Documents.Posting;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.OperationalRegisters;
using NGB.Runtime.Dimensions;
using NGB.Tools.Exceptions;

namespace NGB.AgencyBilling.Runtime.Posting;

public sealed class SalesInvoiceOperationalRegisterPostingHandler(
    IAgencyBillingDocumentReaders readers,
    IAgencyBillingAccountingPolicyReader policyReader,
    IOperationalRegisterRepository registers,
    IDimensionSetService dimensionSets)
    : IDocumentOperationalRegisterPostingHandler
{
    public string TypeCode => AgencyBillingCodes.SalesInvoice;

    public async Task BuildMovementsAsync(
        DocumentRecord document,
        IOperationalRegisterMovementsBuilder builder,
        CancellationToken ct)
    {
        var head = await readers.ReadSalesInvoiceHeadAsync(document.Id, ct);
        var lines = await readers.ReadSalesInvoiceLinesAsync(document.Id, ct);
        var policy = await policyReader.GetRequiredAsync(ct);
        var unbilledRegister = await registers.GetByIdAsync(policy.UnbilledTimeOperationalRegisterId, ct);
        var projectBillingStatusRegister = await registers.GetByIdAsync(policy.ProjectBillingStatusOperationalRegisterId, ct);
        var arOpenItemsRegister = await registers.GetByIdAsync(policy.ArOpenItemsOperationalRegisterId, ct);

        if (unbilledRegister is null)
            throw new NgbConfigurationViolationException($"Operational register '{policy.UnbilledTimeOperationalRegisterId}' was not found.");

        if (projectBillingStatusRegister is null)
            throw new NgbConfigurationViolationException($"Operational register '{policy.ProjectBillingStatusOperationalRegisterId}' was not found.");

        if (arOpenItemsRegister is null)
            throw new NgbConfigurationViolationException($"Operational register '{policy.ArOpenItemsOperationalRegisterId}' was not found.");

        var occurredAtUtc = AgencyBillingPostingCommon.ToOccurredAtUtc(head.DocumentDateUtc);
        var totalAmount = 0m;
        var timesheetHeads = new Dictionary<Guid, AgencyBillingTimesheetHead>();

        foreach (var line in lines)
        {
            var lineAmount = AgencyBillingPostingCommon.ResolveSalesInvoiceLineAmount(line);
            totalAmount += lineAmount;

            if (line.SourceTimesheetId is not { } sourceTimesheetId || sourceTimesheetId == Guid.Empty)
                continue;

            if (!timesheetHeads.TryGetValue(sourceTimesheetId, out var sourceTimesheet))
            {
                sourceTimesheet = await readers.ReadTimesheetHeadAsync(sourceTimesheetId, ct);
                timesheetHeads[sourceTimesheetId] = sourceTimesheet;
            }

            var dimensionSetId = await dimensionSets.GetOrCreateIdAsync(
                AgencyBillingPostingCommon.TimeLedgerBag(
                    head.ClientId,
                    head.ProjectId,
                    sourceTimesheet.TeamMemberId,
                    line.ServiceItemId),
                ct);

            builder.Add(
                unbilledRegister.Code,
                new OperationalRegisterMovement(
                    DocumentId: document.Id,
                    OccurredAtUtc: occurredAtUtc,
                    DimensionSetId: dimensionSetId,
                    Resources: AgencyBillingPostingCommon.BuildUnbilledResources(-line.QuantityHours, -lineAmount)));
        }

        if (totalAmount <= 0m)
            return;

        var projectDimensionSetId = await dimensionSets.GetOrCreateIdAsync(
            AgencyBillingPostingCommon.ProjectBag(head.ClientId, head.ProjectId),
            ct);

        builder.Add(
            projectBillingStatusRegister.Code,
            new OperationalRegisterMovement(
                DocumentId: document.Id,
                OccurredAtUtc: occurredAtUtc,
                DimensionSetId: projectDimensionSetId,
                Resources: AgencyBillingPostingCommon.BuildProjectBillingStatusResources(
                    billedAmountDelta: totalAmount,
                    collectedAmountDelta: 0m,
                    outstandingArAmountDelta: totalAmount)));

        var arOpenItemDimensionSetId = await dimensionSets.GetOrCreateIdAsync(
            AgencyBillingPostingCommon.ArOpenItemBag(head.ClientId, head.ProjectId, document.Id),
            ct);

        builder.Add(
            arOpenItemsRegister.Code,
            new OperationalRegisterMovement(
                DocumentId: document.Id,
                OccurredAtUtc: occurredAtUtc,
                DimensionSetId: arOpenItemDimensionSetId,
                Resources: new Dictionary<string, decimal> { ["amount"] = AgencyBillingPostingCommon.RoundScale4(totalAmount) }));
    }
}
