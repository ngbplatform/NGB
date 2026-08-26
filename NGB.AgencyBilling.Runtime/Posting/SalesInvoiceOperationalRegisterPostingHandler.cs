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
        var sourceTimesheetIds = lines
            .Where(static line => line.SourceTimesheetId is { } id && id != Guid.Empty)
            .Select(static line => line.SourceTimesheetId!.Value)
            .Distinct()
            .ToArray();
        var timesheetHeads = await readers.ReadTimesheetHeadsAsync(sourceTimesheetIds, ct);
        var unbilledDrafts = new List<(decimal Hours, decimal Amount, NGB.Core.Dimensions.DimensionBag Bag)>();

        foreach (var line in lines)
        {
            var lineAmount = AgencyBillingPostingCommon.ResolveSalesInvoiceLineAmount(line);
            totalAmount += lineAmount;

            if (line.SourceTimesheetId is not { } sourceTimesheetId || sourceTimesheetId == Guid.Empty)
                continue;

            if (!timesheetHeads.TryGetValue(sourceTimesheetId, out var sourceTimesheet))
                throw new NgbInvariantViolationException($"Timesheet '{sourceTimesheetId}' is missing its Agency Billing head row.");

            unbilledDrafts.Add((
                line.QuantityHours,
                lineAmount,
                AgencyBillingPostingCommon.TimeLedgerBag(
                    head.ClientId,
                    head.ProjectId,
                    sourceTimesheet.TeamMemberId,
                    line.ServiceItemId)));
        }

        var bags = unbilledDrafts.Select(static x => x.Bag).ToList();
        var projectDimensionSetIndex = -1;
        var arOpenItemDimensionSetIndex = -1;

        if (totalAmount > 0m)
        {
            projectDimensionSetIndex = bags.Count;
            bags.Add(AgencyBillingPostingCommon.ProjectBag(head.ClientId, head.ProjectId));
            arOpenItemDimensionSetIndex = bags.Count;
            bags.Add(AgencyBillingPostingCommon.ArOpenItemBag(head.ClientId, head.ProjectId, document.Id));
        }

        var dimensionSetIds = await dimensionSets.GetOrCreateIdsAsync(bags, ct);

        for (var i = 0; i < unbilledDrafts.Count; i++)
        {
            var draft = unbilledDrafts[i];
            builder.Add(
                unbilledRegister.Code,
                new OperationalRegisterMovement(
                    DocumentId: document.Id,
                    OccurredAtUtc: occurredAtUtc,
                    DimensionSetId: dimensionSetIds[i],
                    Resources: AgencyBillingPostingCommon.BuildUnbilledResources(-draft.Hours, -draft.Amount)));
        }

        if (totalAmount <= 0m)
            return;

        builder.Add(
            projectBillingStatusRegister.Code,
            new OperationalRegisterMovement(
                DocumentId: document.Id,
                OccurredAtUtc: occurredAtUtc,
                DimensionSetId: dimensionSetIds[projectDimensionSetIndex],
                Resources: AgencyBillingPostingCommon.BuildProjectBillingStatusResources(
                    billedAmountDelta: totalAmount,
                    collectedAmountDelta: 0m,
                    outstandingArAmountDelta: totalAmount)));

        builder.Add(
            arOpenItemsRegister.Code,
            new OperationalRegisterMovement(
                DocumentId: document.Id,
                OccurredAtUtc: occurredAtUtc,
                DimensionSetId: dimensionSetIds[arOpenItemDimensionSetIndex],
                Resources: new Dictionary<string, decimal> { ["amount"] = AgencyBillingPostingCommon.RoundScale4(totalAmount) }));
    }
}
