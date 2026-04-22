using NGB.Definitions.Documents.Numbering;

namespace NGB.Runtime.Documents.Policies;

public interface IDocumentNumberingPolicyResolver
{
    IDocumentNumberingPolicy? Resolve(string typeCode);
}
