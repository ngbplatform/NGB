using NGB.Definitions.Documents.Numbering;

namespace NGB.AgencyBilling.Documents.Numbering;

public sealed class AgencyBillingClientContractNumberingPolicy : IDocumentNumberingPolicy
{
    public string TypeCode => AgencyBillingCodes.ClientContract;
    public bool EnsureNumberOnCreateDraft => true;
    public bool EnsureNumberOnPost => false;
}
