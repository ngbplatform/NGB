namespace NGB.Contracts.Admin;

public sealed record MainMenuItemDto(
    string Kind,
    string Code,
    string Label,
    string Route,
    string? Icon = null,
    int Ordinal = 0);

public sealed record MainMenuGroupDto(
    string Label,
    IReadOnlyList<MainMenuItemDto> Items,
    int Ordinal = 0,
    string? Icon = null);

public sealed record MainMenuDto(IReadOnlyList<MainMenuGroupDto> Groups);
