using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.Contracts.Services;

namespace NGB.Application.Abstractions.Services;

/// <summary>Interactive reads with current-user authorization enforced by the implementation.</summary>
public interface IAuthorizedCatalogReadService
{
    Task<IReadOnlyList<CatalogTypeMetadataDto>> GetAllMetadataAsync(CancellationToken ct);
    Task<CatalogTypeMetadataDto> GetTypeMetadataAsync(string catalogType, CancellationToken ct);

    Task<PageResponseDto<CatalogItemDto>> GetPageAsync(string catalogType, PageRequestDto request, CancellationToken ct);
    Task<CatalogItemDto> GetByIdAsync(string catalogType, Guid id, CancellationToken ct);

    Task<IReadOnlyList<CatalogItemDto>> GetHeadItemsByIdsAsync(
        string catalogType,
        IReadOnlyList<Guid> ids,
        CancellationToken ct);

    Task<IReadOnlyList<CatalogLookupDto>> LookupAcrossTypesAsync(
        IReadOnlyList<string> catalogTypes,
        string? query,
        int perTypeLimit,
        bool activeOnly,
        CancellationToken ct);
}
