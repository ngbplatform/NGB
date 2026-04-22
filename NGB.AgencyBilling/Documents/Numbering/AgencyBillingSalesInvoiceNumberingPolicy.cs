using NGB.Definitions.Documents.Numbering;

namespace NGB.AgencyBilling.Documents.Numbering;

public sealed class AgencyBillingSalesInvoiceNumberingPolicy : IDocumentNumberingPolicy
{
    public string TypeCode => AgencyBillingCodes.SalesInvoice;
    public bool EnsureNumberOnCreateDraft => true;
    public bool EnsureNumberOnPost => false;
}
