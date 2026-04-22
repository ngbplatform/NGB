using NGB.Definitions.Documents.Numbering;

namespace NGB.AgencyBilling.Documents.Numbering;

public sealed class AgencyBillingTimesheetNumberingPolicy : IDocumentNumberingPolicy
{
    public string TypeCode => AgencyBillingCodes.Timesheet;
    public bool EnsureNumberOnCreateDraft => true;
    public bool EnsureNumberOnPost => false;
}
