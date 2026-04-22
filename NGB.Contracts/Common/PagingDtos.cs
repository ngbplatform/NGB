namespace NGB.Contracts.Common;

public sealed record PageRequestDto(
    int Offset = 0,
    int Limit = 50,
    string? Search = null,
    IReadOnlyDictionary<string, string>? Filters = null);

public sealed record PageResponseDto<T>(IReadOnlyList<T> Items, int Offset, int Limit, int? Total);
