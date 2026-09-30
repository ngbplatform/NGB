using NGB.Contracts.Common;
using NGB.Contracts.Security;

namespace NGB.Runtime.Security;

public interface IUserAccessManagementService
{
    Task<PageResponseDto<UserListItemDto>> GetUsersAsync(UserPageRequestDto request, CancellationToken ct);

    Task<UserDetailsDto> GetUserAsync(Guid userId, CancellationToken ct);

    Task<UserDetailsDto> CreateUserAsync(CreateUserRequestDto request, CancellationToken ct);

    Task<UserDetailsDto> UpdateUserAsync(Guid userId, UpdateUserRequestDto request, CancellationToken ct);

    Task DeactivateUserAsync(Guid userId, CancellationToken ct);

    Task ReactivateUserAsync(Guid userId, CancellationToken ct);

    Task ReplaceUserRolesAsync(Guid userId, ReplaceUserRolesRequestDto request, CancellationToken ct);
}
