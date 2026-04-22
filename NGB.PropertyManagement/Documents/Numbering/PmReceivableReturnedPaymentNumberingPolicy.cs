using NGB.Definitions.Documents.Numbering;

namespace NGB.PropertyManagement.Documents.Numbering;

public sealed class PmReceivableReturnedPaymentNumberingPolicy : IDocumentNumberingPolicy
{
    public string TypeCode => PropertyManagementCodes.ReceivableReturnedPayment;
    public bool EnsureNumberOnCreateDraft => true;
    public bool EnsureNumberOnPost => false;
}
