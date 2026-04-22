namespace NGB.Contracts.Services;

public sealed record DocumentLookupAcrossTypesRequestDto(
    IReadOnlyList<string> DocumentTypes,
    string? Query,
    int PerTypeLimit = 20,
    bool ActiveOnly = false);
