namespace NGB.Persistence.Ui;

/// <summary>
/// Optional provider capability for resolving every reference kind used by one payload page
/// in a single persistence round-trip.
/// </summary>
public interface IReferencePayloadBatchEnrichmentReader
{
    Task<ReferencePayloadBatchEnrichment> ResolveAsync(
        IReadOnlyCollection<Guid> accountIds,
        IReadOnlyCollection<Guid> operationalRegisterIds,
        IReadOnlyDictionary<string, IReadOnlyCollection<Guid>> catalogIdsByType,
        IReadOnlyCollection<Guid> documentIds,
        CancellationToken ct = default);
}

public sealed record ReferencePayloadBatchEnrichment(
    IReadOnlyDictionary<Guid, string> AccountLabels,
    IReadOnlyDictionary<Guid, string> OperationalRegisterLabels,
    IReadOnlyDictionary<string, IReadOnlyDictionary<Guid, string>> CatalogLabelsByType,
    IReadOnlyDictionary<Guid, string> DocumentLabels);
