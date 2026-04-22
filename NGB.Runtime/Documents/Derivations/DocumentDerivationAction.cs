namespace NGB.Runtime.Documents.Derivations;

/// <summary>
/// A user-facing action available for "Create based on" (aka "Enter based on").
///
/// This DTO is intentionally simple so UI layers can list available actions without pulling
/// implementation details (handler type, etc.).
/// </summary>
public sealed record DocumentDerivationAction(
    string Code,
    string Name,
    string FromTypeCode,
    string ToTypeCode,
    IReadOnlyList<string> RelationshipCodes);
