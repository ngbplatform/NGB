using NGB.Definitions.Documents.Numbering;

namespace NGB.Trade.Documents.Numbering;

public sealed class TradeCustomerReturnNumberingPolicy : IDocumentNumberingPolicy
{
    public string TypeCode => TradeCodes.CustomerReturn;
    public bool EnsureNumberOnCreateDraft => true;
    public bool EnsureNumberOnPost => false;
}
