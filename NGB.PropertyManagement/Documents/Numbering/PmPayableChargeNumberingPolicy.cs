using NGB.Definitions.Documents.Numbering;

namespace NGB.PropertyManagement.Documents.Numbering;

public sealed class PmPayableChargeNumberingPolicy : IDocumentNumberingPolicy
{
    public string TypeCode => PropertyManagementCodes.PayableCharge;
    public bool EnsureNumberOnCreateDraft => true;
    public bool EnsureNumberOnPost => false;
}
