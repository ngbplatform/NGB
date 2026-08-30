using System.Net.Mail;
using NGB.Contracts.Common;
using NGB.Contracts.Security;
using NGB.Core.AuditLog;
using NGB.Core.Security;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Security;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Reporting;
using NGB.Runtime.UnitOfWork;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Security;

public sealed class UserAccessManagementService(
    IUnitOfWork uow,
    IPlatformUserRepository users,
    IPlatformUserRoleRepository userRoles,
    IUserAccessVersionRepository versions,
    IUserProvisioningOperationRepository operations,
    IIdentityProviderUserAdminClient identityProvider,
    IAuditLogService audit)
    : IUserAccessManagementService
{
    internal const int MaxRoleAssignmentsPerUser = 500;
    internal const int MaxUserPageSize = 100;

    public async Task<PageResponseDto<UserListItemDto>> GetUsersAsync(UserPageRequestDto request, CancellationToken ct)
    {
        if (request is null)
            throw new NgbArgumentRequiredException(nameof(request));

        var cursorKind = SpecializedReportCursorCodec.BuildKind(
            "security.users",
            request.IsActive?.ToString());
        var cursor = string.IsNullOrWhiteSpace(request.Cursor)
            ? null
            : SpecializedReportCursorCodec.Decode<PlatformUserPageCursor>(cursorKind, request.Cursor);
        var offset = Math.Clamp(cursor?.Offset ?? request.Offset, 0, PagingLimits.MaxOffset);
        var limit = request.Limit <= 0
            ? PagingLimits.DefaultPageSize
            : Math.Min(request.Limit, MaxUserPageSize);
        var platformPage = cursor is null
            ? await users.GetPageAsync(offset, limit, request.IsActive, ct)
            : await users.GetCursorPageAsync(cursor, limit, request.IsActive, ct);
        var platformUsers = platformPage.Items;
        var userIds = platformUsers.Select(x => x.UserId).ToArray();
        var identityProviderIds = platformUsers
            .Select(static x => NormalizeIdentityProviderId(x.AuthSubject))
            .OfType<string>()
            .ToArray();
        var platformEmails = platformUsers
            .Select(static user => NormalizeIdentityProviderEmail(user.Email))
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var rolesByUserTask = userRoles.GetRolesForUsersAsync(userIds, ct);
        IReadOnlyDictionary<string, IdentityProviderUserDto> identityProviderUsersById;
        IReadOnlyDictionary<string, IdentityProviderUserDto> identityProviderUsersByEmail;

        if (identityProvider is IIdentityProviderBulkUserReader bulkReader)
        {
            var batchTask = bulkReader.GetUsersAsync(identityProviderIds, platformEmails, ct);

            await Task.WhenAll(rolesByUserTask, batchTask);

            var batch = await batchTask;
            identityProviderUsersById = batch.ById;
            identityProviderUsersByEmail = batch.ByEmail;
        }
        else
        {
            var identityProviderUsersByIdTask = identityProvider.GetUsersByIdsAsync(identityProviderIds, ct);

            await Task.WhenAll(rolesByUserTask, identityProviderUsersByIdTask);

            identityProviderUsersById = await identityProviderUsersByIdTask;
            var fallbackEmails = platformUsers
                .Where(user => !HasIdentityProviderUser(identityProviderUsersById, user.AuthSubject))
                .Select(static user => NormalizeIdentityProviderEmail(user.Email))
                .OfType<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            identityProviderUsersByEmail = fallbackEmails.Length == 0
                ? new Dictionary<string, IdentityProviderUserDto>(StringComparer.OrdinalIgnoreCase)
                : await identityProvider.FindUsersByEmailsAsync(fallbackEmails, ct);
        }

        var rolesByUser = await rolesByUserTask;

        var items = platformUsers
            .Select(user =>
            {
                rolesByUser.TryGetValue(user.UserId, out var assignedRoles);
                var keycloakEnabled = ResolveIdentityProviderEnabled(
                    user,
                    identityProviderUsersById,
                    identityProviderUsersByEmail);

                var roles = (assignedRoles ?? [])
                    .OrderBy(static role => role.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                return new UserListItemDto(
                    UserId: user.UserId,
                    AuthSubject: user.AuthSubject,
                    Email: user.Email,
                    DisplayName: user.DisplayName,
                    IsActive: user.IsActive,
                    KeycloakEnabled: keycloakEnabled,
                    Roles: roles.Select(ToRoleBadge).ToArray(),
                    CreatedAtUtc: user.CreatedAtUtc,
                    UpdatedAtUtc: user.UpdatedAtUtc);
            })
            .OrderBy(static x => x.DisplayName ?? x.Email ?? x.AuthSubject, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var hasMore = platformPage.HasMore || offset + items.Length < platformPage.Total;
        var nextCursor = hasMore
            ? SpecializedReportCursorCodec.Encode(
                cursorKind,
                new PlatformUserPageCursor(offset + items.Length, platformPage.Total))
            : null;
        return new PageResponseDto<UserListItemDto>(
            items,
            offset,
            limit,
            checked((int)platformPage.Total),
            hasMore,
            nextCursor);
    }

    public async Task<UserDetailsDto> GetUserAsync(Guid userId, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(userId, ct) ?? throw new SecurityUserNotFoundException(userId);
        var identityProviderUserTask = identityProvider.GetUserByIdAsync(user.AuthSubject, ct);
        var roles = await userRoles.GetRolesForUserAsync(userId, ct);
        var version = await versions.GetAsync(userId, ct);
        var idp = await identityProviderUserTask;

        return ToDetails(user, idp, roles, version?.Version ?? 1);
    }

    public async Task<UserDetailsDto> CreateUserAsync(CreateUserRequestDto request, CancellationToken ct)
    {
        if (request is null)
            throw new NgbArgumentRequiredException(nameof(request));

        EnsureRoleAssignmentLimit(request.RoleIds);

        var email = NormalizeRequiredEmail(request.Email, nameof(request.Email));
        var temporaryPassword = string.IsNullOrWhiteSpace(request.TemporaryPassword)
            ? null
            : request.TemporaryPassword;

        var operationId = Guid.CreateVersion7();

        await WriteOperationAsync(operationId, "CreateUser", email, null, null, "Pending", null, ct);

        IdentityProviderUserDto idpUser;
        try
        {
            var existingIdpUser = await identityProvider.FindUserByEmailAsync(email, ct);
            if (existingIdpUser is null)
            {
                idpUser = await identityProvider.CreateUserAsync(
                    new CreateIdentityProviderUserRequest(
                        email,
                        request.FirstName,
                        request.LastName,
                        request.DisplayName,
                        request.Enabled,
                        temporaryPassword,
                        request.RequirePasswordUpdate),
                    ct);
            }
            else
            {
                await identityProvider.UpdateUserAsync(
                    existingIdpUser.UserId,
                    new UpdateIdentityProviderUserRequest(
                        email,
                        request.FirstName,
                        request.LastName,
                        request.DisplayName,
                        request.Enabled),
                    ct);

                idpUser = await identityProvider.GetUserByIdAsync(existingIdpUser.UserId, ct)
                          ?? existingIdpUser with
                          {
                              Email = email,
                              FirstName = request.FirstName,
                              LastName = request.LastName,
                              DisplayName = ResolveDisplayName(request.DisplayName, request.FirstName, request.LastName, email),
                              Enabled = request.Enabled
                          };
            }

            if (!string.IsNullOrWhiteSpace(temporaryPassword))
            {
                await identityProvider.SetTemporaryPasswordAsync(
                    idpUser.UserId,
                    temporaryPassword,
                    request.RequirePasswordUpdate,
                    ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await WriteOperationAsync(operationId, "CreateUser", email, null, null, "Failed", ex.GetType().Name, ct);
            throw;
        }

        var platformUserId = Guid.Empty;

        await uow.ExecuteInUowTransactionAsync(async innerCt =>
        {
            await operations.UpsertAsync(operationId, "CreateUser", email, idpUser.UserId, null, "KeycloakApplied", null, null, innerCt);
            var savedDisplayName = ResolveDisplayName(request.DisplayName, idpUser.DisplayName, idpUser.Email ?? email);

            platformUserId = await users.UpsertAsync(
                authSubject: idpUser.UserId,
                email: idpUser.Email ?? email,
                displayName: savedDisplayName,
                isActive: idpUser.Enabled,
                innerCt);

            await userRoles.ReplaceUserRolesAsync(platformUserId, request.RoleIds, assignedByUserId: null, innerCt);
            var newRoles = await userRoles.GetRolesForUserAsync(platformUserId, innerCt);
            await versions.GetOrCreateAsync(platformUserId, innerCt);
            await operations.UpsertAsync(operationId, "CreateUser", email, idpUser.UserId, platformUserId, "Completed", null, null, innerCt);

            await audit.WriteAsync(
                AuditEntityKind.SecurityUser,
                platformUserId,
                AuditActionCodes.SecurityUserCreate,
                changes: BuildUserAuditChanges(
                    oldUser: null,
                    newEmail: idpUser.Email ?? email,
                    newDisplayName: savedDisplayName,
                    newIsActive: idpUser.Enabled,
                    oldRoles: [],
                    newRoles,
                    passwordChanged: !string.IsNullOrWhiteSpace(temporaryPassword)),
                metadata: new
                {
                    email = idpUser.Email ?? email,
                    roleIds = request.RoleIds
                },
                ct: innerCt);

            await WriteRoleAssignmentAuditEventsAsync(
                oldRoles: [],
                newRoles,
                ToAuditUser(platformUserId, idpUser.Email ?? email, savedDisplayName),
                innerCt);
        }, ct);

        return await GetUserAsync(platformUserId, ct);
    }

    public async Task<UserDetailsDto> UpdateUserAsync(Guid userId, UpdateUserRequestDto request, CancellationToken ct)
    {
        if (request is null)
            throw new NgbArgumentRequiredException(nameof(request));

        EnsureRoleAssignmentLimit(request.RoleIds);

        var user = await users.GetByIdAsync(userId, ct) ?? throw new SecurityUserNotFoundException(userId);
        var email = NormalizeOptionalEmail(request.Email, nameof(request.Email))
            ?? NormalizeRequiredEmail(user.Email, nameof(request.Email));
        var temporaryPassword = string.IsNullOrWhiteSpace(request.TemporaryPassword)
            ? null
            : request.TemporaryPassword;
        var oldRoles = await userRoles.GetRolesForUserAsync(userId, ct);
        var identityProviderUserId = await ResolveIdentityProviderUserIdAsync(
            user,
            [user.Email, email],
            provisioningEmail => new CreateIdentityProviderUserRequest(
                provisioningEmail,
                request.FirstName,
                request.LastName,
                request.DisplayName,
                request.Enabled,
                TemporaryPassword: null,
                RequirePasswordUpdate: false),
            ct);
        var currentIdentityProviderUser = await identityProvider.GetUserByIdAsync(identityProviderUserId, ct);

        await identityProvider.UpdateUserAsync(
            identityProviderUserId,
            new UpdateIdentityProviderUserRequest(
                email,
                request.FirstName ?? currentIdentityProviderUser?.FirstName,
                request.LastName ?? currentIdentityProviderUser?.LastName,
                request.DisplayName,
                request.Enabled),
            ct);

        var passwordChanged = temporaryPassword is not null;
        if (passwordChanged)
        {
            await identityProvider.SetTemporaryPasswordAsync(
                identityProviderUserId,
                temporaryPassword!,
                request.RequirePasswordUpdate,
                ct);
        }

        await uow.ExecuteInUowTransactionAsync(async innerCt =>
        {
            var savedUserId = await users.UpsertAsync(
                identityProviderUserId,
                email,
                ResolveDisplayName(request.DisplayName, user.DisplayName, email),
                request.Enabled,
                innerCt);

            EnsureSamePlatformUser(userId, savedUserId);

            await userRoles.ReplaceUserRolesAsync(userId, request.RoleIds, assignedByUserId: null, innerCt);
            var newRoles = await userRoles.GetRolesForUserAsync(userId, innerCt);
            await versions.IncrementAsync(userId, innerCt);

            await audit.WriteAsync(
                AuditEntityKind.SecurityUser,
                userId,
                AuditActionCodes.SecurityUserUpdate,
                changes: BuildUserAuditChanges(
                    user,
                    email,
                    ResolveDisplayName(request.DisplayName, user.DisplayName, email),
                    request.Enabled,
                    oldRoles,
                    newRoles,
                    passwordChanged),
                metadata: new
                {
                    email,
                    displayName = request.DisplayName,
                    enabled = request.Enabled,
                    roleIds = request.RoleIds,
                    passwordChanged
                },
                ct: innerCt);

            await WriteRoleAssignmentAuditEventsAsync(
                oldRoles,
                newRoles,
                ToAuditUser(userId, email, ResolveDisplayName(request.DisplayName, user.DisplayName, email)),
                innerCt);
        }, ct);

        return await GetUserAsync(userId, ct);
    }

    public Task DeactivateUserAsync(Guid userId, CancellationToken ct)
        => SetUserActiveAsync(userId, isActive: false, AuditActionCodes.SecurityUserDeactivate, ct);

    public Task ReactivateUserAsync(Guid userId, CancellationToken ct)
        => SetUserActiveAsync(userId, isActive: true, AuditActionCodes.SecurityUserReactivate, ct);

    public async Task ReplaceUserRolesAsync(Guid userId, ReplaceUserRolesRequestDto request, CancellationToken ct)
    {
        if (request is null)
            throw new NgbArgumentRequiredException(nameof(request));

        EnsureRoleAssignmentLimit(request.RoleIds);

        var user = await users.GetByIdAsync(userId, ct) ?? throw new SecurityUserNotFoundException(userId);
        var oldRoles = await userRoles.GetRolesForUserAsync(userId, ct);

        await uow.ExecuteInUowTransactionAsync(async innerCt =>
        {
            await userRoles.ReplaceUserRolesAsync(userId, request.RoleIds, assignedByUserId: null, innerCt);
            var newRoles = await userRoles.GetRolesForUserAsync(userId, innerCt);
            await versions.IncrementAsync(userId, innerCt);
            await audit.WriteAsync(
                AuditEntityKind.SecurityUser,
                userId,
                AuditActionCodes.SecurityUserRolesReplace,
                changes:
                [
                    AuditLogService.Change("roles", ToAuditRoles(oldRoles), ToAuditRoles(newRoles))
                ],
                metadata: new
                {
                    roleIds = request.RoleIds
                },
                ct: innerCt);

            await WriteRoleAssignmentAuditEventsAsync(oldRoles, newRoles, ToAuditUser(user), innerCt);
        }, ct);
    }

    private async Task SetUserActiveAsync(Guid userId, bool isActive, string auditAction, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(userId, ct) ?? throw new SecurityUserNotFoundException(userId);
        var identityProviderUserId = await ResolveIdentityProviderUserIdAsync(
            user,
            [user.Email],
            email => new CreateIdentityProviderUserRequest(
                email,
                null,
                null,
                user.DisplayName,
                isActive,
                null,
                false),
            ct);

        await identityProvider.SetUserEnabledAsync(identityProviderUserId, isActive, ct);

        await uow.ExecuteInUowTransactionAsync(async innerCt =>
        {
            if (StringComparer.Ordinal.Equals(identityProviderUserId, user.AuthSubject))
            {
                await users.SetActiveAsync(userId, isActive, innerCt);
            }
            else
            {
                var savedUserId = await users.UpsertAsync(
                    identityProviderUserId,
                    user.Email,
                    user.DisplayName,
                    isActive,
                    innerCt);

                EnsureSamePlatformUser(userId, savedUserId);
            }

            await versions.IncrementAsync(userId, innerCt);
            await audit.WriteAsync(
                AuditEntityKind.SecurityUser,
                userId,
                auditAction,
                changes:
                [
                    AuditLogService.Change("status", ToAuditStatus(user.IsActive), ToAuditStatus(isActive))
                ],
                metadata: new { isActive },
                ct: innerCt);
        }, ct);
    }

    private async Task WriteOperationAsync(
        Guid operationId,
        string type,
        string? email,
        string? keycloakUserId,
        Guid? platformUserId,
        string status,
        string? error,
        CancellationToken ct)
    {
        await uow.ExecuteInUowTransactionAsync(innerCt => operations.UpsertAsync(
            operationId,
            type,
            email,
            keycloakUserId,
            platformUserId,
            status,
            error,
            requestedByUserId: null,
            innerCt), ct);
    }

    private static UserDetailsDto ToDetails(
        PlatformUser user,
        IdentityProviderUserDto? idp,
        IReadOnlyList<PlatformRole> roles,
        long accessVersion)
        => new(
            UserId: user.UserId,
            AuthSubject: user.AuthSubject,
            Email: idp?.Email ?? user.Email,
            FirstName: idp?.FirstName,
            LastName: idp?.LastName,
            DisplayName: ResolveDisplayName(user.DisplayName, idp?.DisplayName, idp?.Email ?? user.Email),
            IsActive: user.IsActive,
            KeycloakEnabled: idp?.Enabled,
            Roles: roles.Select(ToRoleBadge).ToArray(),
            AccessVersion: accessVersion,
            CreatedAtUtc: user.CreatedAtUtc,
            UpdatedAtUtc: user.UpdatedAtUtc);

    private static RoleBadgeDto ToRoleBadge(PlatformRole role)
        => new(role.RoleId, role.Code, role.Name, role.IsSystem, role.IsActive);

    private static void EnsureRoleAssignmentLimit(IReadOnlyList<Guid> roleIds)
    {
        if (roleIds is null)
            throw new NgbArgumentRequiredException(nameof(roleIds));

        if (roleIds.Count > MaxRoleAssignmentsPerUser)
        {
            throw new NgbArgumentOutOfRangeException(
                nameof(roleIds),
                roleIds.Count,
                $"At most {MaxRoleAssignmentsPerUser:N0} roles are allowed per user.");
        }
    }

    private async Task<string> ResolveIdentityProviderUserIdAsync(
        PlatformUser user,
        IReadOnlyList<string?> emailCandidates,
        Func<string, CreateIdentityProviderUserRequest> createRequest,
        CancellationToken ct)
    {
        var bySubject = await identityProvider.GetUserByIdAsync(user.AuthSubject, ct);
        if (bySubject is not null)
            return bySubject.UserId;

        var normalizedEmails = emailCandidates
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .Select(static x => x!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var usersByEmail = normalizedEmails.Length == 0
            ? new Dictionary<string, IdentityProviderUserDto>(StringComparer.OrdinalIgnoreCase)
            : await identityProvider.FindUsersByEmailsAsync(normalizedEmails, ct);

        foreach (var email in normalizedEmails)
        {
            usersByEmail.TryGetValue(email, out var byEmail);
            byEmail ??= usersByEmail
                .FirstOrDefault(pair => pair.Key.Equals(email, StringComparison.OrdinalIgnoreCase))
                .Value;

            if (byEmail is not null)
                return byEmail.UserId;
        }

        var provisioningEmail = normalizedEmails.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(provisioningEmail))
        {
            var created = await identityProvider.CreateUserAsync(createRequest(provisioningEmail), ct);
            return created.UserId;
        }

        throw new NgbConfigurationViolationException(
            "Linked identity provider user could not be found.",
            new Dictionary<string, object?>
            {
                ["platformUserId"] = user.UserId,
                ["authSubject"] = user.AuthSubject,
                ["email"] = user.Email
        });
    }

    private static void EnsureSamePlatformUser(Guid expectedUserId, Guid savedUserId)
    {
        if (savedUserId == expectedUserId)
            return;

        throw new NgbInvariantViolationException(
            "Identity provider user rebind resolved to a different platform user.",
            new Dictionary<string, object?>
            {
                ["expectedUserId"] = expectedUserId,
                ["savedUserId"] = savedUserId
            });
    }

    private static string NormalizeRequiredEmail(string? email, string paramName)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new NgbArgumentRequiredException(paramName);

        var normalized = email.Trim();

        try
        {
            var parsed = new MailAddress(normalized);
            if (!string.Equals(parsed.Address, normalized, StringComparison.OrdinalIgnoreCase))
                throw new FormatException();
        }
        catch (FormatException)
        {
            throw new NgbArgumentInvalidException(paramName, "Email must be a valid email address.");
        }

        return normalized;
    }

    private static string? NormalizeOptionalEmail(string? email, string paramName)
        => string.IsNullOrWhiteSpace(email)
            ? null
            : NormalizeRequiredEmail(email, paramName);

    private static string? ResolveDisplayName(params string?[] candidates)
        => candidates.FirstOrDefault(static x => !string.IsNullOrWhiteSpace(x))?.Trim();

    private static bool HasIdentityProviderUser(
        IReadOnlyDictionary<string, IdentityProviderUserDto> usersById,
        string? authSubject)
    {
        var key = NormalizeIdentityProviderId(authSubject);
        return key is not null && usersById.ContainsKey(key);
    }

    private static bool? ResolveIdentityProviderEnabled(
        PlatformUser user,
        IReadOnlyDictionary<string, IdentityProviderUserDto> usersById,
        IReadOnlyDictionary<string, IdentityProviderUserDto> usersByEmail)
    {
        var identityProviderId = NormalizeIdentityProviderId(user.AuthSubject);
        if (identityProviderId is not null && usersById.TryGetValue(identityProviderId, out var byId))
            return byId.Enabled;

        var email = NormalizeIdentityProviderEmail(user.Email);
        if (email is not null && usersByEmail.TryGetValue(email, out var byEmail))
            return byEmail.Enabled;

        return false;
    }

    private static string? NormalizeIdentityProviderId(string? identityProviderUserId)
        => string.IsNullOrWhiteSpace(identityProviderUserId) ? null : identityProviderUserId.Trim();

    private static string? NormalizeIdentityProviderEmail(string? email)
        => string.IsNullOrWhiteSpace(email) ? null : email.Trim();

    private async Task WriteRoleAssignmentAuditEventsAsync(
        IReadOnlyList<PlatformRole> oldRoles,
        IReadOnlyList<PlatformRole> newRoles,
        object auditUser,
        CancellationToken ct)
    {
        var oldRoleIds = oldRoles.Select(static role => role.RoleId).ToHashSet();
        var newRoleIds = newRoles.Select(static role => role.RoleId).ToHashSet();
        var requests = new List<AuditLogWriteRequest>();

        foreach (var role in newRoles.Where(role => !oldRoleIds.Contains(role.RoleId)))
        {
            requests.Add(new AuditLogWriteRequest(
                AuditEntityKind.SecurityRole,
                role.RoleId,
                AuditActionCodes.SecurityRoleUpdate,
                Changes:
                [
                    AuditLogService.Change("assigned_users", null, auditUser)
                ],
                Metadata: new
                {
                    assignment = "added",
                    roleCode = role.Code,
                    user = auditUser
                }));
        }

        foreach (var role in oldRoles.Where(role => !newRoleIds.Contains(role.RoleId)))
        {
            requests.Add(new AuditLogWriteRequest(
                AuditEntityKind.SecurityRole,
                role.RoleId,
                AuditActionCodes.SecurityRoleUpdate,
                Changes:
                [
                    AuditLogService.Change("assigned_users", auditUser, null)
                ],
                Metadata: new
                {
                    assignment = "removed",
                    roleCode = role.Code,
                    user = auditUser
                }));
        }

        if (requests.Count > 0)
            await audit.WriteBatchAsync(requests, ct);
    }

    private static IReadOnlyList<AuditFieldChange> BuildUserAuditChanges(
        PlatformUser? oldUser,
        string? newEmail,
        string? newDisplayName,
        bool newIsActive,
        IReadOnlyList<PlatformRole> oldRoles,
        IReadOnlyList<PlatformRole> newRoles,
        bool passwordChanged)
    {
        var changes = new List<AuditFieldChange>
        {
            AuditLogService.Change("email", oldUser?.Email, newEmail),
            AuditLogService.Change("display_name", oldUser?.DisplayName, newDisplayName),
            AuditLogService.Change("status", oldUser is null ? null : ToAuditStatus(oldUser.IsActive), ToAuditStatus(newIsActive)),
            AuditLogService.Change("roles", ToAuditRoles(oldRoles), ToAuditRoles(newRoles))
        };

        if (passwordChanged)
            changes.Add(AuditLogService.Change("password", null, "Changed"));

        return changes;
    }

    private static object ToAuditUser(PlatformUser user) => ToAuditUser(user.UserId, user.Email, user.DisplayName);

    private static object ToAuditUser(Guid userId, string? email, string? displayName)
        => new
        {
            display = ResolveDisplayName(displayName, email, userId.ToString()),
            email,
            userId
        };

    private static object[] ToAuditRoles(IReadOnlyList<PlatformRole> roles)
        => roles
            .OrderBy(static role => role.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static role => role.Code, StringComparer.OrdinalIgnoreCase)
            .Select(static role => new
            {
                display = role.Name,
                code = role.Code,
                status = ToAuditStatus(role.IsActive)
            })
            .Cast<object>()
            .ToArray();

    private static string ToAuditStatus(bool isActive) => isActive ? "Active" : "Inactive";
}
