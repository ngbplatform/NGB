using NGB.Metadata.Schema;

namespace NGB.Persistence.Schema;

/// <summary>
/// Provider-specific schema inspector. Implementations should fetch schema in bulk (few queries)
/// and return a snapshot used for in-memory validation.
/// </summary>
public interface IDbSchemaInspector
{
    Task<DbSchemaSnapshot> GetSnapshotAsync(CancellationToken ct = default);
}
