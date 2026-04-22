using NGB.Definitions.Documents.Numbering;

namespace NGB.Trade.Documents.Numbering;

public sealed class TradeVendorPaymentNumberingPolicy : IDocumentNumberingPolicy
{
    public string TypeCode => TradeCodes.VendorPayment;
    public bool EnsureNumberOnCreateDraft => true;
    public bool EnsureNumberOnPost => false;
}
