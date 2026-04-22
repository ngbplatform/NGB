using Microsoft.AspNetCore.Mvc;
using NGB.Api.Internal;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Effects;
using NGB.Contracts.Graph;
using NGB.Contracts.Metadata;
using NGB.Contracts.Services;

namespace NGB.Api.Controllers;

public abstract class DocumentControllerBase(IDocumentService service) : ControllerBase
{
    [HttpGet("~/api/documents/metadata")]
    public Task<IReadOnlyList<DocumentTypeMetadataDto>> GetAllMetadata(CancellationToken ct)
        => service.GetAllMetadataAsync(ct);

    [HttpGet("~/api/documents/{documentType}/metadata")]
    public Task<DocumentTypeMetadataDto> GetTypeMetadata([FromRoute] string documentType, CancellationToken ct)
        => service.GetTypeMetadataAsync(documentType, ct);

    [HttpGet]
    public Task<PageResponseDto<DocumentDto>> GetPage([FromRoute] string documentType, CancellationToken ct)
        => service.GetPageAsync(documentType, QueryParsing.ToPageRequest(Request.Query), ct);

    [HttpGet("{id:guid}")]
    public Task<DocumentDto> GetById([FromRoute] string documentType, [FromRoute] Guid id, CancellationToken ct)
        => service.GetByIdAsync(documentType, id, ct);

    [HttpPost]
    public Task<DocumentDto> CreateDraft(
        [FromRoute] string documentType,
        [FromBody] RecordPayload payload,
        CancellationToken ct)
        => service.CreateDraftAsync(documentType, payload, ct);

    [HttpPut("{id:guid}")]
    public Task<DocumentDto> UpdateDraft(
        [FromRoute] string documentType,
        [FromRoute] Guid id,
        [FromBody] RecordPayload payload,
        CancellationToken ct)
        => service.UpdateDraftAsync(documentType, id, payload, ct);

    [HttpDelete("{id:guid}")]
    public Task DeleteDraft([FromRoute] string documentType, [FromRoute] Guid id, CancellationToken ct)
        => service.DeleteDraftAsync(documentType, id, ct);

    [HttpPost("{id:guid}/post")]
    public Task<DocumentDto> Post([FromRoute] string documentType, [FromRoute] Guid id, CancellationToken ct)
        => service.PostAsync(documentType, id, ct);

    [HttpPost("{id:guid}/unpost")]
    public Task<DocumentDto> Unpost([FromRoute] string documentType, [FromRoute] Guid id, CancellationToken ct)
        => service.UnpostAsync(documentType, id, ct);

    [HttpPost("{id:guid}/repost")]
    public Task<DocumentDto> Repost([FromRoute] string documentType, [FromRoute] Guid id, CancellationToken ct)
        => service.RepostAsync(documentType, id, ct);

    [HttpPost("{id:guid}/mark-for-deletion")]
    public Task<DocumentDto> MarkForDeletion([FromRoute] string documentType, [FromRoute] Guid id, CancellationToken ct)
        => service.MarkForDeletionAsync(documentType, id, ct);

    [HttpPost("{id:guid}/unmark-for-deletion")]
    public Task<DocumentDto> UnmarkForDeletion([FromRoute] string documentType, [FromRoute] Guid id, CancellationToken ct)
        => service.UnmarkForDeletionAsync(documentType, id, ct);

    [HttpPost("{id:guid}/actions/{actionCode}")]
    public Task<DocumentDto> ExecuteAction(
        [FromRoute] string documentType,
        [FromRoute] Guid id,
        [FromRoute] string actionCode,
        CancellationToken ct)
        => service.ExecuteActionAsync(documentType, id, actionCode, ct);

    [HttpGet("{id:guid}/derive-actions")]
    public Task<IReadOnlyList<DocumentDerivationActionDto>> GetDerivationActions(
        [FromRoute] string documentType,
        [FromRoute] Guid id,
        CancellationToken ct)
        => service.GetDerivationActionsAsync(documentType, id, ct);

    [HttpGet("{id:guid}/graph")]
    public Task<RelationshipGraphDto> GetGraph(
        [FromRoute] string documentType,
        [FromRoute] Guid id,
        [FromQuery] int depth = 2,
        [FromQuery] int maxNodes = 200,
        CancellationToken ct = default)
        => service.GetRelationshipGraphAsync(documentType, id, depth, maxNodes, ct);

    [HttpGet("{id:guid}/effects")]
    public Task<DocumentEffectsDto> GetEffects(
        [FromRoute] string documentType,
        [FromRoute] Guid id,
        [FromQuery] int limit = 500,
        CancellationToken ct = default)
        => service.GetEffectsAsync(documentType, id, limit, ct);

    [HttpPost("~/api/documents/lookup")]
    public Task<IReadOnlyList<DocumentLookupDto>> LookupAcrossTypes(
        [FromBody] DocumentLookupAcrossTypesRequestDto request,
        CancellationToken ct)
        => service.LookupAcrossTypesAsync(request.DocumentTypes, request.Query, request.PerTypeLimit, request.ActiveOnly, ct);

    [HttpPost("~/api/documents/lookup/by-ids")]
    public Task<IReadOnlyList<DocumentLookupDto>> GetByIdsAcrossTypes(
        [FromBody] DocumentLookupByIdsRequestDto request,
        CancellationToken ct)
        => service.GetByIdsAcrossTypesAsync(request.DocumentTypes, request.Ids, ct);

    [HttpPost("~/api/documents/{targetDocumentType}/derive")]
    public Task<DocumentDto> Derive(
        [FromRoute] string targetDocumentType,
        [FromBody] DeriveRequestDto request,
        CancellationToken ct)
        => service.DeriveAsync(targetDocumentType, request.SourceDocumentId, request.RelationshipType, request.InitialPayload, ct);
}
