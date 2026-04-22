namespace NGB.PropertyManagement.Contracts.Catalogs;

public sealed record PropertyBulkCreateUnitsResponse(
    Guid BuildingId,
    int RequestedCount,
    int CreatedCount,
    int DuplicateCount,
    IReadOnlyList<Guid> CreatedIds,
    IReadOnlyList<string> CreatedUnitNosSample,
    IReadOnlyList<string> DuplicateUnitNosSample)
{
    /// <summary>
    /// Number of units that would be created (for dry-run) or were created (for a real run).
    /// </summary>
    public int WouldCreateCount { get; init; }

    /// <summary>
    /// Sample of generated unit_no values for UI preview.
    /// </summary>
    public IReadOnlyList<string> PreviewUnitNosSample { get; init; } = [];

    public bool IsDryRun { get; init; }
}
