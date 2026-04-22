using NGB.Contracts.Common;
using NGB.Contracts.Effects;
using NGB.Contracts.Graph;
using NGB.Contracts.Metadata;
using NGB.Contracts.Services;

namespace NGB.Application.Abstractions.Services;

public interface IDocumentService
{
    Task<IReadOnlyList<DocumentTypeMetadataDto>> GetAllMetadataAsync(CancellationToken ct);
    Task<DocumentTypeMetadataDto> GetTypeMetadataAsync(string documentType, CancellationToken ct);

    Task<PageResponseDto<DocumentDto>> GetPageAsync(string documentType, PageRequestDto request, CancellationToken ct);
    Task<DocumentDto> GetByIdAsync(string documentType, Guid id, CancellationToken ct);
    Task<IReadOnlyList<DocumentLookupDto>> LookupAcrossTypesAsync(
        IReadOnlyList<string> documentTypes,
        string? query,
        int perTypeLimit,
        bool activeOnly,
        CancellationToken ct);
    Task<IReadOnlyList<DocumentLookupDto>> GetByIdsAcrossTypesAsync(
        IReadOnlyList<string> documentTypes,
        IReadOnlyList<Guid> ids,
        CancellationToken ct);

    Task<DocumentDto> CreateDraftAsync(string documentType, RecordPayload payload, CancellationToken ct);
    Task<DocumentDto> UpdateDraftAsync(string documentType, Guid id, RecordPayload payload, CancellationToken ct);
    Task DeleteDraftAsync(string documentType, Guid id, CancellationToken ct);

    Task<DocumentDto> PostAsync(string documentType, Guid id, CancellationToken ct);
    Task<DocumentDto> UnpostAsync(string documentType, Guid id, CancellationToken ct);
    Task<DocumentDto> RepostAsync(string documentType, Guid id, CancellationToken ct);

    Task<DocumentDto> MarkForDeletionAsync(string documentType, Guid id, CancellationToken ct);
    Task<DocumentDto> UnmarkForDeletionAsync(string documentType, Guid id, CancellationToken ct);

    Task<DocumentDto> ExecuteActionAsync(string documentType, Guid id, string actionCode, CancellationToken ct);

    Task<IReadOnlyList<DocumentDerivationActionDto>> GetDerivationActionsAsync(
        string documentType,
        Guid id,
        CancellationToken ct);

    Task<RelationshipGraphDto> GetRelationshipGraphAsync(
        string documentType,
        Guid id,
        int depth,
        int maxNodes,
        CancellationToken ct);
    
    Task<DocumentEffectsDto> GetEffectsAsync(string documentType, Guid id, int limit, CancellationToken ct);

    Task<DocumentDto> DeriveAsync(
        string targetDocumentType,
        Guid sourceDocumentId,
        string relationshipType,
        RecordPayload? initialPayload,
        CancellationToken ct);
}
