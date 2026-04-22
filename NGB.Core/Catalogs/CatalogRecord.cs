using NGB.Core.Base;

namespace NGB.Core.Catalogs;

/// <summary>
/// Catalog registry record (table: catalogs).
///
/// This is the common header shared by all catalog types.
/// Per-type data belongs in separate tables:
///   cat_{catalog_code}, cat_{catalog_code}__{part}, ...
/// </summary>
public sealed class CatalogRecord : Entity
{
    public required string CatalogCode { get; init; }

    public required bool IsDeleted { get; init; }

    public required DateTime CreatedAtUtc { get; init; }

    public required DateTime UpdatedAtUtc { get; init; }
}
