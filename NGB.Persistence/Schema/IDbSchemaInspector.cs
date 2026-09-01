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

/// <summary>
/// Optional provider capability for sharing one consistent schema snapshot across a bounded
/// validation batch. Standalone calls outside the lease must continue to read a fresh snapshot.
/// </summary>
public interface IDbSchemaSnapshotScopeFactory
{
    ValueTask<IAsyncDisposable> BeginSnapshotScopeAsync(CancellationToken ct = default);
}
