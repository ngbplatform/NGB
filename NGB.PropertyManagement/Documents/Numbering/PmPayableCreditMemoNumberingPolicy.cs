using NGB.Definitions.Documents.Numbering;

namespace NGB.PropertyManagement.Documents.Numbering;

public sealed class PmPayableCreditMemoNumberingPolicy : IDocumentNumberingPolicy
{
    public string TypeCode => PropertyManagementCodes.PayableCreditMemo;
    public bool EnsureNumberOnCreateDraft => true;
    public bool EnsureNumberOnPost => false;
}
