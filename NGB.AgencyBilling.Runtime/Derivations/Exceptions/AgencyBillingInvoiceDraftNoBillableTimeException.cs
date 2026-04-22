using NGB.Tools.Exceptions;

namespace NGB.AgencyBilling.Runtime.Derivations.Exceptions;

public sealed class AgencyBillingInvoiceDraftNoBillableTimeException(Guid sourceTimesheetId)
    : NgbConflictException(
        message: "Generate Invoice Draft requires billable timesheet lines with a positive billable amount.",
        errorCode: "ab.invoice_draft.no_billable_time",
        context: new Dictionary<string, object?>
        {
            ["sourceTimesheetId"] = sourceTimesheetId
        });
