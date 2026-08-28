namespace NGB.Contracts.Common;

public static class PagingLimits
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 500;
    public const int MaxOffset = 100_000;
    public const int MaxMaterializedRows = 10_000;
    public const int MaxLookupIds = 500;
    public const int MaxLookupTypes = 50;
    public const int MaxPerTypeLookupLimit = 100;

    public static int BoundOffset(int offset) => Math.Clamp(offset, 0, MaxOffset);
}

public sealed record PageRequestDto(
    int Offset = 0,
    int Limit = PagingLimits.DefaultPageSize,
    string? Search = null,
    IReadOnlyDictionary<string, string>? Filters = null,
    string? Cursor = null);

public sealed record PageResponseDto<T>(
    IReadOnlyList<T> Items,
    int Offset,
    int Limit,
    int? Total,
    bool HasMore = false,
    string? NextCursor = null);
