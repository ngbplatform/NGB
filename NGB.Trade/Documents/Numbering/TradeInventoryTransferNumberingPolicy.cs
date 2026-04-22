using NGB.Definitions.Documents.Numbering;

namespace NGB.Trade.Documents.Numbering;

public sealed class TradeInventoryTransferNumberingPolicy : IDocumentNumberingPolicy
{
    public string TypeCode => TradeCodes.InventoryTransfer;
    public bool EnsureNumberOnCreateDraft => true;
    public bool EnsureNumberOnPost => false;
}
