namespace NGB.Contracts.Services;

public sealed record ByIdsRequestDto(IReadOnlyList<Guid> Ids);
