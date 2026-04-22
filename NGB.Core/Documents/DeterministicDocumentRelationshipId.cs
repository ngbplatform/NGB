using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.Core.Documents;

/// <summary>
/// Deterministically computes <c>document_relationships.relationship_id</c> from the canonical
/// relationship triplet: <c>fromDocumentId</c>, normalized relationship code, <c>toDocumentId</c>.
///
/// IMPORTANT:
/// - Use <see cref="From"/> when you have a human-facing relationship code that still needs trim/lower normalization.
/// - Use <see cref="FromNormalizedCode"/> when the caller already has the persisted <c>relationship_code_norm</c> value.
/// - The algorithm MUST remain stable because existing persisted rows and tests rely on it.
/// </summary>
public static class DeterministicDocumentRelationshipId
{
    public static Guid From(Guid fromDocumentId, string relationshipCode, Guid toDocumentId)
    {
        fromDocumentId.EnsureRequired(nameof(fromDocumentId));
        toDocumentId.EnsureRequired(nameof(toDocumentId));

        if (string.IsNullOrWhiteSpace(relationshipCode))
            throw new NgbArgumentRequiredException(nameof(relationshipCode));

        return FromNormalizedCode(fromDocumentId, NormalizeRelationshipCodeNorm(relationshipCode), toDocumentId);
    }

    public static Guid FromNormalizedCode(Guid fromDocumentId, string relationshipCodeNorm, Guid toDocumentId)
    {
        fromDocumentId.EnsureRequired(nameof(fromDocumentId));
        toDocumentId.EnsureRequired(nameof(toDocumentId));

        if (string.IsNullOrWhiteSpace(relationshipCodeNorm))
            throw new NgbArgumentRequiredException(nameof(relationshipCodeNorm));

        var codeNorm = NormalizeRelationshipCodeNorm(relationshipCodeNorm);
        return DeterministicGuid.Create($"DocumentRelationship|{fromDocumentId:D}|{codeNorm}|{toDocumentId:D}");
    }

    public static string NormalizeRelationshipCodeNorm(string relationshipCode)
    {
        if (string.IsNullOrWhiteSpace(relationshipCode))
            throw new NgbArgumentRequiredException(nameof(relationshipCode));

        return relationshipCode.Trim().ToLowerInvariant();
    }
}
