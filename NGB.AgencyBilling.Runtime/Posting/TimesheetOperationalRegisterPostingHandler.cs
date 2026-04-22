using NGB.AgencyBilling.Documents;
using NGB.Core.Documents;
using NGB.Definitions.Documents.Posting;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.OperationalRegisters;
using NGB.AgencyBilling.Runtime.Policy;
using NGB.Runtime.Dimensions;
using NGB.Tools.Exceptions;

namespace NGB.AgencyBilling.Runtime.Posting;

public sealed class TimesheetOperationalRegisterPostingHandler(
    IAgencyBillingDocumentReaders readers,
    IAgencyBillingAccountingPolicyReader policyReader,
    IOperationalRegisterRepository registers,
    IDimensionSetService dimensionSets)
    : IDocumentOperationalRegisterPostingHandler
{
    public string TypeCode => AgencyBillingCodes.Timesheet;

    public async Task BuildMovementsAsync(
        DocumentRecord document,
        IOperationalRegisterMovementsBuilder builder,
        CancellationToken ct)
    {
        var head = await readers.ReadTimesheetHeadAsync(document.Id, ct);
        var lines = await readers.ReadTimesheetLinesAsync(document.Id, ct);
        var policy = await policyReader.GetRequiredAsync(ct);
        var projectTimeRegister = await registers.GetByIdAsync(policy.ProjectTimeLedgerOperationalRegisterId, ct);
        var unbilledRegister = await registers.GetByIdAsync(policy.UnbilledTimeOperationalRegisterId, ct);

        if (projectTimeRegister is null)
            throw new NgbConfigurationViolationException($"Operational register '{policy.ProjectTimeLedgerOperationalRegisterId}' was not found.");

        if (unbilledRegister is null)
            throw new NgbConfigurationViolationException($"Operational register '{policy.UnbilledTimeOperationalRegisterId}' was not found.");

        var occurredAtUtc = AgencyBillingPostingCommon.ToOccurredAtUtc(head.WorkDate);

        foreach (var line in lines)
        {
            var dimensionSetId = await dimensionSets.GetOrCreateIdAsync(
                AgencyBillingPostingCommon.TimeLedgerBag(head.ClientId, head.ProjectId, head.TeamMemberId, line.ServiceItemId),
                ct);

            var lineAmount = AgencyBillingPostingCommon.ResolveTimesheetLineAmount(line);
            var lineCostAmount = AgencyBillingPostingCommon.ResolveTimesheetLineCostAmount(line);

            builder.Add(
                projectTimeRegister.Code,
                new OperationalRegisterMovement(
                    DocumentId: document.Id,
                    OccurredAtUtc: occurredAtUtc,
                    DimensionSetId: dimensionSetId,
                    Resources: AgencyBillingPostingCommon.BuildProjectTimeResources(
                        line.Hours,
                        line.Billable,
                        lineAmount,
                        lineCostAmount)));

            if (!line.Billable)
                continue;

            builder.Add(
                unbilledRegister.Code,
                new OperationalRegisterMovement(
                    DocumentId: document.Id,
                    OccurredAtUtc: occurredAtUtc,
                    DimensionSetId: dimensionSetId,
                    Resources: AgencyBillingPostingCommon.BuildUnbilledResources(line.Hours, lineAmount)));
        }
    }
}
