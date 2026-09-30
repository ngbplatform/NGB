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
/// Optional optimized read boundary for providers that can resolve a mixed page of subjects and emails
/// with provider-specific bounded concurrency and without an unbounded directory scan.
/// </summary>
public interface IIdentityProviderBulkUserReader
{
    Task<IdentityProviderUserBatch> GetUsersAsync(
        IReadOnlyList<string> identityProviderUserIds,
        IReadOnlyList<string> emails,
        CancellationToken ct);
}

/// <summary>
/// Non-blocking page enrichment boundary. Implementations must only read an in-process projection or cache;
/// a list request must never turn into one remote identity-provider call per row.
/// </summary>
public interface IIdentityProviderUserPageSnapshotReader
{
    IdentityProviderUserBatch GetCachedUsers(IReadOnlyList<string> identityProviderUserIds, IReadOnlyList<string> emails);
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
