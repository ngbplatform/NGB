namespace NGB.Runtime.Documents.Numbering;

public interface IDocumentNumberFormatter
{
    string Format(string typeCode, int fiscalYear, long sequence);
}
