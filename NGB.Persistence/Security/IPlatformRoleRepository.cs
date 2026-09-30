using NGB.Core.Security;

namespace NGB.Persistence.Security;

public interface IPlatformRoleRepository
{
    Task<IReadOnlyList<PlatformRole>> GetAllAsync(CancellationToken ct = default);

    Task<IReadOnlyList<PlatformRoleListRecord>> GetListAsync(int limit, CancellationToken ct = default);

    Task<PlatformRole?> GetByIdAsync(Guid roleId, CancellationToken ct = default);

    Task<PlatformRole?> GetByCodeAsync(string code, CancellationToken ct = default);

    Task<PlatformRole> UpsertAsync(
        Guid roleId,
        string code,
        string name,
        string? description,
        bool isSystem,
        bool isActive,
        CancellationToken ct = default);

    Task SetActiveAsync(Guid roleId, bool isActive, CancellationToken ct = default);

    Task<IReadOnlyDictionary<Guid, int>> GetAssignedUserCountsAsync(CancellationToken ct = default);
}

public sealed record PlatformRoleListRecord(PlatformRole Role, int AssignedUserCount);
