namespace NGB.Runtime.Security;

public sealed record IdentityProviderUserDto(
    string UserId,
    string? Email,
    string? FirstName,
    string? LastName,
    string? DisplayName,
    bool Enabled);

public sealed record CreateIdentityProviderUserRequest(
    string Email,
    string? FirstName,
    string? LastName,
    string? DisplayName,
    bool Enabled,
    string? TemporaryPassword,
    bool RequirePasswordUpdate);

public sealed record UpdateIdentityProviderUserRequest(
    string? Email,
    string? FirstName,
    string? LastName,
    string? DisplayName,
    bool Enabled);

public interface IIdentityProviderUserAdminClient
{
    Task<IdentityProviderUserDto> CreateUserAsync(CreateIdentityProviderUserRequest request, CancellationToken ct);

    Task UpdateUserAsync(string identityProviderUserId, UpdateIdentityProviderUserRequest request, CancellationToken ct);

    Task SetUserEnabledAsync(string identityProviderUserId, bool enabled, CancellationToken ct);

    Task<IdentityProviderUserDto?> GetUserByIdAsync(string identityProviderUserId, CancellationToken ct);

    Task<IdentityProviderUserDto?> FindUserByEmailAsync(string email, CancellationToken ct);

    Task SetTemporaryPasswordAsync(
        string identityProviderUserId,
        string temporaryPassword,
        bool requireUpdate,
        CancellationToken ct);
}
