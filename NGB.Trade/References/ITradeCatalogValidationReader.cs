namespace NGB.Trade.References;

/// <summary>
/// Minimal batch projection used by posting validation. It deliberately avoids loading
/// complete catalog payloads and tabular parts for every document line.
/// </summary>
public interface ITradeCatalogValidationReader
{
    Task<IReadOnlyDictionary<Guid, TradeInventoryItemValidationSnapshot>> GetInventoryItemsAsync(
        IReadOnlyCollection<Guid> itemIds,
        CancellationToken ct = default);
}

public sealed record TradeInventoryItemValidationSnapshot(
    Guid ItemId,
    bool IsDeleted,
    bool? IsActive,
    bool? IsInventoryItem);
