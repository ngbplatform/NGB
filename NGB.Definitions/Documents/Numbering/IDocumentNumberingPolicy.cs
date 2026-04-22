namespace NGB.Definitions.Documents.Numbering;

public interface IDocumentNumberingPolicy
{
    string TypeCode { get; }
    bool EnsureNumberOnCreateDraft { get; }
    bool EnsureNumberOnPost { get; }
}
