using NGB.Definitions.Documents.Numbering;

namespace NGB.Trade.Documents.Numbering;

public sealed class TradeCustomerPaymentNumberingPolicy : IDocumentNumberingPolicy
{
    public string TypeCode => TradeCodes.CustomerPayment;
    public bool EnsureNumberOnCreateDraft => true;
    public bool EnsureNumberOnPost => false;
}
