namespace NGB.Contracts.Services;

public sealed record DocumentDerivationActionDto(
    string Code,
    string Name,
    string FromTypeCode,
    string ToTypeCode,
    IReadOnlyList<string> RelationshipCodes);
