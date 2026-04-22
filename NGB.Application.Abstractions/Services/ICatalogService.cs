using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.Contracts.Services;

namespace NGB.Application.Abstractions.Services;

public interface ICatalogService
{
    Task<IReadOnlyList<CatalogTypeMetadataDto>> GetAllMetadataAsync(CancellationToken ct);
    Task<CatalogTypeMetadataDto> GetTypeMetadataAsync(string catalogType, CancellationToken ct);

    Task<PageResponseDto<CatalogItemDto>> GetPageAsync(string catalogType, PageRequestDto request, CancellationToken ct);
    Task<CatalogItemDto> GetByIdAsync(string catalogType, Guid id, CancellationToken ct);
    Task<IReadOnlyList<CatalogLookupDto>> LookupAcrossTypesAsync(
        IReadOnlyList<string> catalogTypes,
        string? query,
        int perTypeLimit,
        bool activeOnly,
        CancellationToken ct);

    Task<CatalogItemDto> CreateAsync(string catalogType, RecordPayload payload, CancellationToken ct);
    Task<CatalogItemDto> UpdateAsync(string catalogType, Guid id, RecordPayload payload, CancellationToken ct);

    Task MarkForDeletionAsync(string catalogType, Guid id, CancellationToken ct);
    Task UnmarkForDeletionAsync(string catalogType, Guid id, CancellationToken ct);

    Task<IReadOnlyList<LookupItemDto>> LookupAsync(string catalogType, string? query, int limit, CancellationToken ct);
    Task<IReadOnlyList<LookupItemDto>> GetByIdsAsync(string catalogType, IReadOnlyList<Guid> ids, CancellationToken ct);
}
