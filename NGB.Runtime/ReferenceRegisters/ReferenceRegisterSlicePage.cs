namespace NGB.Runtime.ReferenceRegisters;

/// <summary>
/// Cursor-aware result for slice paging. The cursor advances by the last *examined* key (dimension_set_id),
/// which may be greater than the last returned visible record when tombstones are skipped.
/// </summary>
public sealed record ReferenceRegisterSlicePage<T>(
    IReadOnlyList<T> Records,
    Guid? NextAfterDimensionSetId,
    bool HasMore);
