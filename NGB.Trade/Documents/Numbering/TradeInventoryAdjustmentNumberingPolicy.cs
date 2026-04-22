using NGB.Definitions.Documents.Numbering;

namespace NGB.Trade.Documents.Numbering;

public sealed class TradeInventoryAdjustmentNumberingPolicy : IDocumentNumberingPolicy
{
    public string TypeCode => TradeCodes.InventoryAdjustment;
    public bool EnsureNumberOnCreateDraft => true;
    public bool EnsureNumberOnPost => false;
}
