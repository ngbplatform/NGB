namespace NGB.Contracts.Services;

public sealed record DocumentLookupByIdsRequestDto(
    IReadOnlyList<string> DocumentTypes,
    IReadOnlyList<Guid> Ids);
