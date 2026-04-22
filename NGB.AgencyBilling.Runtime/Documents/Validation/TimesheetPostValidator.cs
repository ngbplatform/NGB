using NGB.AgencyBilling.Documents;
using NGB.AgencyBilling.References;
using NGB.AgencyBilling.Runtime.Posting;
using NGB.AgencyBilling.Runtime.Validation;
using NGB.Core.Documents;
using NGB.Definitions.Documents.Validation;
using NGB.Tools.Exceptions;

namespace NGB.AgencyBilling.Runtime.Documents.Validation;

public sealed class TimesheetPostValidator(
    IAgencyBillingDocumentReaders readers,
    IAgencyBillingReferenceReaders references)
    : IDocumentPostValidator
{
    public string TypeCode => AgencyBillingCodes.Timesheet;

    public async Task ValidateBeforePostAsync(DocumentRecord documentForUpdate, CancellationToken ct)
    {
        AgencyBillingDocumentValidatorBindingGuard.EnsureExpectedType(documentForUpdate, TypeCode, nameof(TimesheetPostValidator));

        var head = await readers.ReadTimesheetHeadAsync(documentForUpdate.Id, ct);
        var lines = await readers.ReadTimesheetLinesAsync(documentForUpdate.Id, ct);

        await AgencyBillingCatalogValidationGuards.EnsureClientAsync(head.ClientId, "client_id", references, ct, requireOperationallyActive: true);
        var project = await AgencyBillingCatalogValidationGuards.EnsureProjectAsync(head.ProjectId, "project_id", references, ct, requireOperationallyActive: true);
        await AgencyBillingCatalogValidationGuards.EnsureTeamMemberAsync(head.TeamMemberId, "team_member_id", references, ct);
        AgencyBillingCatalogValidationGuards.EnsureProjectBelongsToClient(project, head.ClientId, "project_id", "client_id");

        if (lines.Count == 0)
            throw new NgbArgumentInvalidException("lines", "Timesheet must contain at least one line.");

        var expectedHours = 0m;
        var expectedAmount = 0m;
        var expectedCostAmount = 0m;

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var prefix = $"lines[{i}]";

            if (line.ServiceItemId is { } serviceItemId && serviceItemId != Guid.Empty)
                await AgencyBillingCatalogValidationGuards.EnsureServiceItemAsync(serviceItemId, $"{prefix}.service_item_id", references, ct);

            if (line.Hours <= 0m)
                throw new NgbArgumentInvalidException($"{prefix}.hours", "Hours must be greater than zero.");

            if (line.CostRate is null || line.CostRate < 0m)
                throw new NgbArgumentInvalidException($"{prefix}.cost_rate", "Cost Rate Snapshot is required and must be zero or greater.");

            if (line.LineCostAmount is null || line.LineCostAmount < 0m)
                throw new NgbArgumentInvalidException($"{prefix}.line_cost_amount", "Line Cost Amount is required and must be zero or greater.");

            var expectedLineCost = AgencyBillingPostingCommon.RoundScale4(line.Hours * line.CostRate.Value);
            if (AgencyBillingPostingCommon.RoundScale4(line.LineCostAmount.Value) != expectedLineCost)
            {
                throw new NgbArgumentInvalidException(
                    $"{prefix}.line_cost_amount",
                    $"Line Cost Amount must equal Hours x Cost Rate ({expectedLineCost:0.####}).");
            }

            expectedHours += line.Hours;
            expectedCostAmount += line.LineCostAmount.Value;

            if (line.Billable)
            {
                if (line.BillingRate is null || line.BillingRate <= 0m)
                    throw new NgbArgumentInvalidException($"{prefix}.billing_rate", "Billing Rate Snapshot is required and must be greater than zero for billable time.");

                if (line.LineAmount is null || line.LineAmount <= 0m)
                    throw new NgbArgumentInvalidException($"{prefix}.line_amount", "Line Amount is required and must be greater than zero for billable time.");

                var expectedLineAmount = AgencyBillingPostingCommon.RoundScale4(line.Hours * line.BillingRate.Value);
                if (AgencyBillingPostingCommon.RoundScale4(line.LineAmount.Value) != expectedLineAmount)
                {
                    throw new NgbArgumentInvalidException(
                        $"{prefix}.line_amount",
                        $"Line Amount must equal Hours x Billing Rate ({expectedLineAmount:0.####}).");
                }

                expectedAmount += line.LineAmount.Value;
            }
            else if (line.LineAmount is not null && AgencyBillingPostingCommon.RoundScale4(line.LineAmount.Value) != 0m)
            {
                throw new NgbArgumentInvalidException($"{prefix}.line_amount", "Non-billable time must not carry billable amount.");
            }
        }

        if (AgencyBillingPostingCommon.RoundScale4(head.TotalHours) != AgencyBillingPostingCommon.RoundScale4(expectedHours))
            throw new NgbArgumentInvalidException("total_hours", "Total Hours must equal the sum of line hours.");

        if (AgencyBillingPostingCommon.RoundScale4(head.Amount) != AgencyBillingPostingCommon.RoundScale4(expectedAmount))
            throw new NgbArgumentInvalidException("amount", "Amount must equal the sum of billable line amounts.");

        if (AgencyBillingPostingCommon.RoundScale4(head.CostAmount) != AgencyBillingPostingCommon.RoundScale4(expectedCostAmount))
            throw new NgbArgumentInvalidException("cost_amount", "Cost Amount must equal the sum of line cost amounts.");
    }
}
