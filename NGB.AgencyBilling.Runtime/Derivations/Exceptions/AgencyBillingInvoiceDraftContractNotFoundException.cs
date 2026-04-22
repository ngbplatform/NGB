using NGB.Tools.Exceptions;

namespace NGB.AgencyBilling.Runtime.Derivations.Exceptions;

public sealed class AgencyBillingInvoiceDraftContractNotFoundException(
    Guid sourceTimesheetId,
    Guid clientId,
    Guid projectId,
    DateOnly workDate)
    : NgbConflictException(
        message: "Generate Invoice Draft requires an active posted client contract for the timesheet client, project, and work date.",
        errorCode: "ab.invoice_draft.contract_not_found",
        context: new Dictionary<string, object?>
        {
            ["sourceTimesheetId"] = sourceTimesheetId,
            ["clientId"] = clientId,
            ["projectId"] = projectId,
            ["workDate"] = workDate
        });
