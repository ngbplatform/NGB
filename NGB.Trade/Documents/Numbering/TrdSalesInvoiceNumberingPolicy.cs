using NGB.Definitions.Documents.Numbering;

namespace NGB.Trade.Documents.Numbering;

public sealed class TrdSalesInvoiceNumberingPolicy : IDocumentNumberingPolicy
{
    public string TypeCode => TradeCodes.SalesInvoice;
    public bool EnsureNumberOnCreateDraft => true;
    public bool EnsureNumberOnPost => false;
}
