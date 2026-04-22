using NGB.Tools.Exceptions;

namespace NGB.AgencyBilling.Runtime.Derivations.Exceptions;

public sealed class AgencyBillingInvoiceDraftAlreadyExistsException(Guid sourceTimesheetId)
    : NgbValidationException(
        message: MessageText,
        errorCode: "ab.invoice_draft.already_exists",
        context: BuildContext(sourceTimesheetId))
{
    private const string MessageText = "An invoice draft or posted invoice already exists for this timesheet.";

    private static IReadOnlyDictionary<string, object?> BuildContext(Guid sourceTimesheetId)
        => new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["sourceTimesheetId"] = sourceTimesheetId,
            ["errors"] = new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["_form"] = [MessageText]
            }
        };
}
