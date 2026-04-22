using NGB.Definitions.Documents.Numbering;

namespace NGB.Trade.Documents.Numbering;

public sealed class TrdPurchaseReceiptNumberingPolicy : IDocumentNumberingPolicy
{
    public string TypeCode => TradeCodes.PurchaseReceipt;
    public bool EnsureNumberOnCreateDraft => true;
    public bool EnsureNumberOnPost => false;
}
