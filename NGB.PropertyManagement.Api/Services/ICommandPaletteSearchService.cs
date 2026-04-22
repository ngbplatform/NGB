using NGB.Contracts.Search;

namespace NGB.PropertyManagement.Api.Services;

public interface ICommandPaletteSearchService
{
    Task<CommandPaletteSearchResponseDto> SearchAsync(CommandPaletteSearchRequestDto request, CancellationToken ct);
}
