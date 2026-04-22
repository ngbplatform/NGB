using NGB.Contracts.Services;
using NGB.Persistence.Catalogs.Universal;
using NGB.Persistence.Documents.Universal;

namespace NGB.Runtime.Ui;

/// <summary>
/// No-op implementation used by unit tests or lightweight hosts.
/// Returns items unchanged (no reference enrichment).
/// </summary>
public sealed class NoOpReferencePayloadEnricher : IReferencePayloadEnricher
{
    public static readonly NoOpReferencePayloadEnricher Instance = new();

    private NoOpReferencePayloadEnricher() { }

    public Task<IReadOnlyList<CatalogItemDto>> EnrichCatalogItemsAsync(
        CatalogHeadDescriptor ownerHead,
        string ownerTypeCode,
        IReadOnlyList<CatalogItemDto> items,
        CancellationToken ct)
        => Task.FromResult(items);

    public Task<IReadOnlyList<DocumentDto>> EnrichDocumentItemsAsync(
        DocumentHeadDescriptor ownerHead,
        string ownerTypeCode,
        IReadOnlyList<DocumentDto> items,
        CancellationToken ct)
        => Task.FromResult(items);
}
