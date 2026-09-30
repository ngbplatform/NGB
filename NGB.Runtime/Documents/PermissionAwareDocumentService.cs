using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Effects;
using NGB.Contracts.Graph;
using NGB.Contracts.Metadata;
using NGB.Contracts.Services;
using NGB.Core.Security;
using NGB.Runtime.Security;

namespace NGB.Runtime.Documents;

public sealed class PermissionAwareDocumentService(
    IDocumentService inner,
    INgbAccessChecker access,
    NgbSecurityCache cache)
    : IDocumentService
{
    public async Task<IReadOnlyList<DocumentTypeMetadataDto>> GetAllMetadataAsync(CancellationToken ct)
    {
        var snapshot = await access.GetSnapshotAsync(ct);
        if (!snapshot.HasAny(NgbResourceKinds.Document, NgbPermissionActions.View))
            return [];

        return await cache.GetOrCreateDocumentMetadataAsync(
            snapshot,
            async token =>
            {
                var metadata = await inner.GetAllMetadataAsync(token);
                return FilterMetadata(metadata, snapshot);
            },
            ct) ?? [];
    }

    public async Task<DocumentTypeMetadataDto> GetTypeMetadataAsync(string documentType, CancellationToken ct)
    {
        var snapshot = await access.GetSnapshotAsync(ct);
        Require(snapshot, documentType, NgbPermissionActions.View);

        return await cache.GetOrCreateDocumentTypeMetadataAsync(
            snapshot,
            documentType,
            async token =>
            {
                var metadata = await inner.GetTypeMetadataAsync(documentType, token);
                return ApplyCapabilities(metadata, snapshot);
            },
            ct) ?? throw new InvalidOperationException("Document metadata cache returned no value.");
    }

    public async Task<PageResponseDto<DocumentDto>> GetPageAsync(
        string documentType,
        PageRequestDto request,
        CancellationToken ct)
    {
        await RequireAsync(documentType, NgbPermissionActions.View, ct);
        return await inner.GetPageAsync(documentType, request, ct);
    }

    public async Task<DocumentDto> GetByIdAsync(string documentType, Guid id, CancellationToken ct)
    {
        await RequireAsync(documentType, NgbPermissionActions.View, ct);
        return await inner.GetByIdAsync(documentType, id, ct);
    }

    public async Task<IReadOnlyList<DocumentLookupDto>> LookupAcrossTypesAsync(
        IReadOnlyList<string> documentTypes,
        string? query,
        int perTypeLimit,
        bool activeOnly,
        CancellationToken ct)
    {
        var allowed = await FilterAsync(documentTypes, NgbPermissionActions.Lookup, ct);
        return allowed.Count == 0
            ? []
            : await inner.LookupAcrossTypesAsync(allowed, query, perTypeLimit, activeOnly, ct);
    }

    public async Task<IReadOnlyList<DocumentLookupDto>> GetByIdsAcrossTypesAsync(
        IReadOnlyList<string> documentTypes,
        IReadOnlyList<Guid> ids,
        CancellationToken ct)
    {
        var allowed = await FilterAsync(documentTypes, NgbPermissionActions.Lookup, ct);
        return allowed.Count == 0
            ? []
            : await inner.GetByIdsAcrossTypesAsync(allowed, ids, ct);
    }

    public async Task<DocumentDto> CreateDraftAsync(string documentType, RecordPayload payload, CancellationToken ct)
    {
        await RequireAsync(documentType, NgbPermissionActions.Create, ct);
        return await inner.CreateDraftAsync(documentType, payload, ct);
    }

    public async Task<DocumentDto> UpdateDraftAsync(
        string documentType,
        Guid id,
        RecordPayload payload,
        CancellationToken ct)
    {
        await RequireAsync(documentType, NgbPermissionActions.EditDraft, ct);
        return await inner.UpdateDraftAsync(documentType, id, payload, ct);
    }

    public async Task DeleteDraftAsync(string documentType, Guid id, CancellationToken ct)
    {
        await RequireAsync(documentType, NgbPermissionActions.DeleteDraft, ct);
        await inner.DeleteDraftAsync(documentType, id, ct);
    }

    public async Task<DocumentDto> ExecuteActionAsync(string documentType, Guid id, string actionCode, CancellationToken ct)
    {
        await RequireAsync(documentType, actionCode, ct);
        return await inner.ExecuteActionAsync(documentType, id, actionCode, ct);
    }

    public async Task<RelationshipGraphDto> GetRelationshipGraphAsync(
        string documentType,
        Guid id,
        int depth,
        int maxNodes,
        CancellationToken ct)
    {
        await RequireAsync(documentType, NgbPermissionActions.ViewFlow, ct);
        return await inner.GetRelationshipGraphAsync(documentType, id, depth, maxNodes, ct);
    }

    public async Task<DocumentEffectsDto> GetEffectsAsync(string documentType, Guid id, int limit, CancellationToken ct)
    {
        await RequireAsync(documentType, NgbPermissionActions.ViewEffects, ct);
        return await inner.GetEffectsAsync(documentType, id, limit, ct);
    }

    public async Task<DocumentDto> DeriveAsync(
        string targetDocumentType,
        Guid sourceDocumentId,
        string relationshipType,
        RecordPayload? initialPayload,
        CancellationToken ct)
    {
        await RequireAsync(targetDocumentType, NgbPermissionActions.Create, ct);
        return await inner.DeriveAsync(targetDocumentType, sourceDocumentId, relationshipType, initialPayload, ct);
    }

    private Task RequireAsync(string documentType, string action, CancellationToken ct)
        => access.RequireAsync(NgbResourceKinds.Document, documentType, action, ct);

    private static IReadOnlyList<DocumentTypeMetadataDto> FilterMetadata(
        IReadOnlyList<DocumentTypeMetadataDto> metadata,
        PermissionSnapshot snapshot)
    {
        var result = new List<DocumentTypeMetadataDto>(metadata.Count);
        foreach (var item in metadata)
        {
            if (Has(snapshot, item.DocumentType, NgbPermissionActions.View))
                result.Add(ApplyCapabilities(item, snapshot));
        }

        return result;
    }

    internal static DocumentTypeMetadataDto ApplyCapabilities(
        DocumentTypeMetadataDto metadata,
        PermissionSnapshot snapshot)
    {
        var documentType = metadata.DocumentType;
        var current = metadata.Capabilities ?? new DocumentCapabilitiesDto();
        var actions = FilterActions(metadata, snapshot);

        return metadata with
        {
            Actions = actions,
            Capabilities = current with
            {
                CanCreate = current.CanCreate && Has(snapshot, documentType, NgbPermissionActions.Create),
                CanEditDraft = current.CanEditDraft && Has(snapshot, documentType, NgbPermissionActions.EditDraft),
                CanDeleteDraft = current.CanDeleteDraft && Has(snapshot, documentType, NgbPermissionActions.DeleteDraft),
                CanPost = current.CanPost && Has(snapshot, documentType, NgbPermissionActions.Post),
                CanUnpost = current.CanUnpost && Has(snapshot, documentType, NgbPermissionActions.Unpost),
                CanRepost = current.CanRepost && Has(snapshot, documentType, NgbPermissionActions.Repost),
                CanMarkForDeletion = current.CanMarkForDeletion && Has(snapshot, documentType, NgbPermissionActions.MarkForDeletion),
                CanViewEffects = current.CanViewEffects && Has(snapshot, documentType, NgbPermissionActions.ViewEffects),
                CanViewFlow = current.CanViewFlow && Has(snapshot, documentType, NgbPermissionActions.ViewFlow),
                SupportsActions = current.SupportsActions && actions.Count > 0
            }
        };
    }

    private static IReadOnlyList<ActionMetadataDto> FilterActions(
        DocumentTypeMetadataDto metadata,
        PermissionSnapshot snapshot)
    {
        if (metadata.Actions is null || metadata.Actions.Count == 0)
            return [];

        var result = new List<ActionMetadataDto>(metadata.Actions.Count);
        foreach (var action in metadata.Actions)
        {
            if (Has(snapshot, metadata.DocumentType, action.Code))
                result.Add(action);
        }

        return result;
    }

    private async Task<IReadOnlyList<string>> FilterAsync(
        IReadOnlyList<string> documentTypes,
        string action,
        CancellationToken ct)
    {
        if (documentTypes is null || documentTypes.Count == 0)
            return [];

        var snapshot = await access.GetSnapshotAsync(ct);
        var result = new List<string>(documentTypes.Count);
        foreach (var documentType in documentTypes.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (Has(snapshot, documentType, action))
                result.Add(documentType);
        }

        return result;
    }

    private static void Require(PermissionSnapshot snapshot, string documentType, string action)
    {
        var permission = new NgbPermissionKey(NgbResourceKinds.Document, documentType, action);
        if (!snapshot.Has(permission))
            throw new NgbPermissionDeniedException(permission);
    }

    private static bool Has(PermissionSnapshot snapshot, string documentType, string action)
        => snapshot.Has(NgbResourceKinds.Document, documentType, action);
}
