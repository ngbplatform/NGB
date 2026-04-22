using NGB.Core.Documents;

namespace NGB.Definitions.Documents.Approval;

public interface IDocumentApprovalPolicy
{
    string TypeCode { get; }

    Task EnsureCanPostAsync(DocumentRecord documentForUpdate, CancellationToken ct = default);
}
