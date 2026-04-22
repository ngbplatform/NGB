using NGB.Tools.Exceptions;

namespace NGB.Core.Documents.Exceptions;

public sealed class DocumentDerivationInvariantViolationException(string reason, Guid derivedDraftId)
    : NgbInfrastructureException(
        message: $"Document derivation invariant violated ({reason}). derivedDraftId={derivedDraftId}.",
        errorCode: Code,
        context: new Dictionary<string, object?>
        {
            ["reason"] = reason,
            ["derivedDraftId"] = derivedDraftId
        })
{
    public const string Code = "doc.derivation.invariant_violation";

    public string Reason { get; } = reason;
    public Guid DerivedDraftId { get; } = derivedDraftId;
}
