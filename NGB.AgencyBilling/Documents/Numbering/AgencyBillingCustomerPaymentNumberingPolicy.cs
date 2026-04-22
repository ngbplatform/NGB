using NGB.Definitions.Documents.Numbering;

namespace NGB.AgencyBilling.Documents.Numbering;

public sealed class AgencyBillingCustomerPaymentNumberingPolicy : IDocumentNumberingPolicy
{
    public string TypeCode => AgencyBillingCodes.CustomerPayment;
    public bool EnsureNumberOnCreateDraft => true;
    public bool EnsureNumberOnPost => false;
}
