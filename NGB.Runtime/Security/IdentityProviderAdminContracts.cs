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

public sealed record IdentityProviderUserBatch(
    IReadOnlyDictionary<string, IdentityProviderUserDto> ById,
    IReadOnlyDictionary<string, IdentityProviderUserDto> ByEmail);

/// <summary>
/// Optional optimized read boundary for providers that can scan/list users in pages instead of issuing
/// one remote request per platform user.
/// </summary>
public interface IIdentityProviderBulkUserReader
{
    Task<IdentityProviderUserBatch> GetUsersAsync(
        IReadOnlyList<string> identityProviderUserIds,
        IReadOnlyList<string> emails,
        CancellationToken ct);
}

public interface IIdentityProviderUserAdminClient
{
    Task<IdentityProviderUserDto> CreateUserAsync(CreateIdentityProviderUserRequest request, CancellationToken ct);

    Task UpdateUserAsync(string identityProviderUserId, UpdateIdentityProviderUserRequest request, CancellationToken ct);

    Task SetUserEnabledAsync(string identityProviderUserId, bool enabled, CancellationToken ct);

    Task<IdentityProviderUserDto?> GetUserByIdAsync(string identityProviderUserId, CancellationToken ct);

    Task<IReadOnlyDictionary<string, IdentityProviderUserDto>> GetUsersByIdsAsync(
        IReadOnlyList<string> identityProviderUserIds,
        CancellationToken ct);

    Task<IdentityProviderUserDto?> FindUserByEmailAsync(string email, CancellationToken ct);

    Task<IReadOnlyDictionary<string, IdentityProviderUserDto>> FindUsersByEmailsAsync(
        IReadOnlyList<string> emails,
        CancellationToken ct);

    Task SetTemporaryPasswordAsync(
        string identityProviderUserId,
        string temporaryPassword,
        bool requireUpdate,
        CancellationToken ct);
}
