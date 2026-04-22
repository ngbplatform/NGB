namespace NGB.Contracts.Search;

public sealed record CommandPaletteSearchContextDto(
    string? EntityType = null,
    string? DocumentType = null,
    string? CatalogType = null,
    Guid? EntityId = null);

public sealed record CommandPaletteSearchRequestDto(
    string? Query,
    string? Scope = null,
    int Limit = 20,
    string? CurrentRoute = null,
    CommandPaletteSearchContextDto? Context = null);

public sealed record CommandPaletteResultItemDto(
    string Key,
    string Kind,
    string Title,
    string? Subtitle,
    string? Icon,
    string? Badge,
    string? Route,
    string? CommandCode,
    string? Status,
    bool OpenInNewTabSupported,
    decimal Score);

public sealed record CommandPaletteGroupDto(string Code, string Label, IReadOnlyList<CommandPaletteResultItemDto> Items);

public sealed record CommandPaletteSearchResponseDto(IReadOnlyList<CommandPaletteGroupDto> Groups);
