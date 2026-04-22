using NGB.Definitions.Documents.Numbering;

namespace NGB.Trade.Documents.Numbering;

public sealed class TrdItemPriceUpdateNumberingPolicy : IDocumentNumberingPolicy
{
    public string TypeCode => TradeCodes.ItemPriceUpdate;
    public bool EnsureNumberOnCreateDraft => true;
    public bool EnsureNumberOnPost => false;
}
