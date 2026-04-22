using NGB.Definitions.Documents.Approval;

namespace NGB.Runtime.Documents.Policies;

public interface IDocumentApprovalPolicyResolver
{
    IDocumentApprovalPolicy? Resolve(string typeCode);
}
