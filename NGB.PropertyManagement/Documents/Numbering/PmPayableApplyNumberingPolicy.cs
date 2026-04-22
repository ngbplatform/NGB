using NGB.Definitions.Documents.Numbering;

namespace NGB.PropertyManagement.Documents.Numbering;

public sealed class PmPayableApplyNumberingPolicy : IDocumentNumberingPolicy
{
    public string TypeCode => PropertyManagementCodes.PayableApply;
    public bool EnsureNumberOnCreateDraft => true;
    public bool EnsureNumberOnPost => false;
}
