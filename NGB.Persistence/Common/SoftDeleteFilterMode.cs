namespace NGB.Persistence.Common;

/// <summary>
/// Soft-delete view mode for paging queries.
/// - <see cref="All"/>: no filter
/// - <see cref="Active"/>: exclude soft-deleted rows
/// - <see cref="Deleted"/>: only soft-deleted rows
/// </summary>
public enum SoftDeleteFilterMode
{
    All = 0,
    Active = 1,
    Deleted = 2,
}
