using NGB.Definitions.Documents.Numbering;

namespace NGB.Trade.Documents.Numbering;

public sealed class TradeVendorReturnNumberingPolicy : IDocumentNumberingPolicy
{
    public string TypeCode => TradeCodes.VendorReturn;
    public bool EnsureNumberOnCreateDraft => true;
    public bool EnsureNumberOnPost => false;
}
