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

/// <summary>
/// Provider capability that also receives the candidate document types for every referenced id.
/// This lets typed-table providers avoid generating query branches for unrelated document types.
/// </summary>
public interface IReferencePayloadTypedBatchEnrichmentReader : IReferencePayloadBatchEnrichmentReader
{
    Task<ReferencePayloadBatchEnrichment> ResolveAsync(
        IReadOnlyCollection<Guid> accountIds,
        IReadOnlyCollection<Guid> operationalRegisterIds,
        IReadOnlyDictionary<string, IReadOnlyCollection<Guid>> catalogIdsByType,
        IReadOnlyDictionary<string, IReadOnlyCollection<Guid>> documentIdsByType,
        CancellationToken ct = default);
}

public sealed record ReferencePayloadBatchEnrichment(
    IReadOnlyDictionary<Guid, string> AccountLabels,
    IReadOnlyDictionary<Guid, string> OperationalRegisterLabels,
    IReadOnlyDictionary<string, IReadOnlyDictionary<Guid, string>> CatalogLabelsByType,
    IReadOnlyDictionary<Guid, string> DocumentLabels);
