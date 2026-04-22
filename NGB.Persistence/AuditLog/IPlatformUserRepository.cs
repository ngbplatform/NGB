using NGB.Core.AuditLog;

namespace NGB.Persistence.AuditLog;

/// <summary>
/// Projection of external identity provider users.
/// </summary>
public interface IPlatformUserRepository
{
    /// <summary>
    /// Inserts or updates a user by <paramref name="authSubject"/> and returns the stable user_id.
    /// </summary>
    Task<Guid> UpsertAsync(
        string authSubject,
        string? email,
        string? displayName,
        bool isActive,
        CancellationToken ct = default);

    Task<PlatformUser?> GetByAuthSubjectAsync(string authSubject, CancellationToken ct = default);

    Task<IReadOnlyDictionary<Guid, PlatformUser>> GetByIdsAsync(
        IReadOnlyList<Guid> userIds,
        CancellationToken ct = default);
}
