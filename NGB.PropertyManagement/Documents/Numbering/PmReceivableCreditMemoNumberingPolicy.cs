using NGB.Definitions.Documents.Numbering;

namespace NGB.PropertyManagement.Documents.Numbering;

public sealed class PmReceivableCreditMemoNumberingPolicy : IDocumentNumberingPolicy
{
    public string TypeCode => PropertyManagementCodes.ReceivableCreditMemo;
    public bool EnsureNumberOnCreateDraft => true;
    public bool EnsureNumberOnPost => false;
}
