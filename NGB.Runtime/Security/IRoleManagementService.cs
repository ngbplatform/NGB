using NGB.Contracts.Security;

namespace NGB.Runtime.Security;

public interface IRoleManagementService
{
    Task<IReadOnlyList<RoleListItemDto>> GetRolesAsync(CancellationToken ct);

    Task<RoleDetailsDto> GetRoleAsync(Guid roleId, CancellationToken ct);

    Task<RoleDetailsDto> CreateRoleAsync(CreateRoleRequestDto request, CancellationToken ct);

    Task<RoleDetailsDto> UpdateRoleAsync(Guid roleId, UpdateRoleRequestDto request, CancellationToken ct);

    Task DeactivateRoleAsync(Guid roleId, CancellationToken ct);

    Task ReactivateRoleAsync(Guid roleId, CancellationToken ct);

    Task ReplaceRolePermissionsAsync(Guid roleId, ReplaceRolePermissionsRequestDto request, CancellationToken ct);
}
